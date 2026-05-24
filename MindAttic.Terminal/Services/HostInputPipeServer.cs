using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using MindAttic.Terminal.Interop;

namespace MindAttic.Terminal.Services;

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
        : this(PipeName(providerKey, Process.GetCurrentProcess().Id), ConsoleInputInjector.InjectText)
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
                using var server = new NamedPipeServerStream(
                    Name,
                    PipeDirection.In,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(ct).ConfigureAwait(false);

                using var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: false);
                var text = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(text))
                    sink(text);
            }
            catch (OperationCanceledException) { return; }
            catch
            {
                // A broken connection or transient pipe failure shouldn't take
                // the agent down; just loop and accept the next client.
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
