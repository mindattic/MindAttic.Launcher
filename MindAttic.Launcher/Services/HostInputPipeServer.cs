using System.IO.Pipes;
using System.Text;
using MindAttic.Launcher.Interop;

namespace MindAttic.Launcher.Services;

/// <summary>
/// Background loop that listens on a deterministic per-tab named pipe and
/// injects any received text into the host's console input buffer. The pipe
/// name is <c>mindattic-host-{provider}-{pid}</c>, lowercased on the provider
/// so the launcher can enumerate just Claude tabs by prefix match.
/// </summary>
public sealed class HostInputPipeServer : IDisposable
{
    public const string PipeNamePrefix = "mindattic-host-";

    public static string PipeName(string providerKey, int pid) =>
        $"{PipeNamePrefix}{providerKey.ToLowerInvariant()}-{pid}";

    private readonly CancellationTokenSource cts = new();
    private readonly Task loop;
    private readonly Action<string> sink;

    public string Name { get; }

    public HostInputPipeServer(string providerKey)
        : this(PipeName(providerKey, Environment.ProcessId), ConsoleInputInjector.InjectText)
    {
    }

    /// <summary>Test seam — caller supplies the pipe name and the sink that
    /// receives injected text. Production callers should use the single-arg ctor.</summary>
    public HostInputPipeServer(string pipeName, Action<string> sink)
    {
        Name = pipeName;
        this.sink = sink;
        loop = Task.Run(() => RunLoop(cts.Token));
    }

    private async Task RunLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                // maxNumberOfServerInstances: 1 so another local process can't
                // pre-create or squat the same pipe name and intercept the
                // broadcaster's keystroke payload — pipe name is PID-derived
                // and known, so the squat surface was real.
                using var server = new NamedPipeServerStream(
                    Name,
                    PipeDirection.In,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(ct).ConfigureAwait(false);

                using var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: false);
                var text = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(text))
                    sink(text);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                // A broken connection or transient pipe failure shouldn't take
                // the agent down — but log it so a silent injection failure
                // (WriteConsoleInputW returning false) isn't invisible.
                try { System.Console.Error.WriteLine($"[host-pipe] {ex.Message}"); } catch { }

                // Back off before retrying. If the failure is persistent — the
                // pipe name is squatted, or NamedPipeServerStream creation keeps
                // throwing — looping straight back would peg a core and flood
                // stderr. The delay honours cancellation so Dispose still exits
                // promptly.
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
