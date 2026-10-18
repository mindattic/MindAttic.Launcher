using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace MindAttic.Launcher.Services;

/// <summary>A request to restart a hosted CLI in place, resuming its session, so it bills
/// <see cref="Billing"/> (<see cref="ClaudeBillingMode.Team"/> or <see cref="ClaudeBillingMode.Api"/>).</summary>
public sealed record RestartResumeRequest(string Billing, string Session, int CliPid, string? Cwd);

/// <summary>
/// Background loop on the tab's control pipe, <c>mindattic-control-{provider}-{pid}</c>: the channel
/// MindAttic.Orchestrator uses to ask the host for things, never typed into the CLI the way the input
/// pipe's text is (<see cref="HostInputPipeServer"/>). It sits outside the <c>mindattic-host-</c> prefix so
/// nothing that scans for tabs takes it for one. Each connection carries one UTF-8 JSON line; a
/// <c>restart-resume</c> request goes to the sink, and any other type is ignored. Nothing is sent back.
/// The contract is the Orchestrator's docs/ORCHESTRATION.md, "The Launcher's control pipe".
/// </summary>
public sealed class HostControlPipeServer : IDisposable
{
    public const string PipeNamePrefix = "mindattic-control-";

    public static string PipeName(string providerKey, int pid) =>
        $"{PipeNamePrefix}{providerKey.ToLowerInvariant()}-{pid}";

    private readonly CancellationTokenSource cts = new();
    private readonly Task loop;
    private readonly Action<RestartResumeRequest> sink;

    public string Name { get; }

    /// <summary>This host's control pipe for <paramref name="providerKey"/>'s tab.</summary>
    public static HostControlPipeServer ForProvider(string providerKey, Action<RestartResumeRequest> sink) =>
        new(PipeName(providerKey, Environment.ProcessId), sink);

    /// <summary>Test seam — caller supplies the pipe name. Production callers use <see cref="ForProvider"/>.</summary>
    public HostControlPipeServer(string pipeName, Action<RestartResumeRequest> sink)
    {
        Name = pipeName;
        this.sink = sink;
        loop = Task.Run(() => RunLoop(cts.Token));
    }

    /// <summary>The request in <paramref name="text"/>, or null when it isn't a well-formed restart-resume.</summary>
    public static RestartResumeRequest? Parse(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text.Trim());
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (Str(root, "type") != "restart-resume") return null;
            var billing = Str(root, "billing")?.ToLowerInvariant();
            if (billing is not (ClaudeBillingMode.Team or ClaudeBillingMode.Api)) return null;
            var session = Str(root, "session");
            if (string.IsNullOrWhiteSpace(session)) return null;
            if (!root.TryGetProperty("cliPid", out var pid) || pid.ValueKind != JsonValueKind.Number || !pid.TryGetInt32(out var cliPid))
                return null;
            return new RestartResumeRequest(billing, session, cliPid, Str(root, "cwd"));
        }
        catch (JsonException)
        {
            return null;
        }

        static string? Str(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }

    private async Task RunLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                // Single instance, as the input pipe: another process can't squat the known name.
                using var server = new NamedPipeServerStream(
                    Name,
                    PipeDirection.In,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(ct).ConfigureAwait(false);

                using var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: false);
                var text = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
                if (Parse(text) is { } request)
                    sink(request);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                try { System.Console.Error.WriteLine($"[control-pipe] {ex.Message}"); } catch { }
                try { await Task.Delay(TimeSpan.FromMilliseconds(500), ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
            }
        }
    }

    public void Dispose()
    {
        cts.Cancel();
        try { loop.Wait(TimeSpan.FromSeconds(1)); } catch { }
        cts.Dispose();
    }
}
