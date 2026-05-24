using System.IO.Pipes;
using System.Text;

namespace MindAttic.Terminal.Services;

/// <summary>
/// Discovers every running <c>mindattic host</c> tab whose pipe matches the
/// configured provider prefix and writes a single payload to each. Used by the
/// "Remote Control" menu to type <c>/remote-control</c> into every open Claude
/// tab at once so the user can hand off to their phone/iPad.
/// </summary>
public sealed class RemoteControlBroadcaster
{
    private readonly Func<IEnumerable<string>> pipeEnumerator;

    public RemoteControlBroadcaster() : this(EnumerateLocalPipes) { }

    /// <summary>Test seam — caller supplies the pipe directory enumeration.</summary>
    public RemoteControlBroadcaster(Func<IEnumerable<string>> pipeEnumerator)
    {
        this.pipeEnumerator = pipeEnumerator;
    }

    public sealed class Result
    {
        public int Delivered { get; init; }
        public IReadOnlyList<string> Failed { get; init; } = [];
    }

    public async Task<Result> BroadcastAsync(string providerKey, string payload, CancellationToken ct = default)
    {
        var prefix = $"{HostInputPipeServer.PipeNamePrefix}{providerKey.ToLowerInvariant()}-";
        var targets = pipeEnumerator()
            .Select(ExtractPipeName)
            .Where(n => n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var delivered = 0;
        var failed = new List<string>();
        foreach (var name in targets)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", name, PipeDirection.Out, PipeOptions.Asynchronous);
                await client.ConnectAsync(1000, ct).ConfigureAwait(false);
                var bytes = Encoding.UTF8.GetBytes(payload);
                await client.WriteAsync(bytes, ct).ConfigureAwait(false);
                await client.FlushAsync(ct).ConfigureAwait(false);
                delivered++;
            }
            catch
            {
                failed.Add(name);
            }
        }

        return new Result { Delivered = delivered, Failed = failed };
    }

    private static IEnumerable<string> EnumerateLocalPipes()
    {
        try { return Directory.GetFiles(@"\\.\pipe\"); }
        catch { return []; }
    }

    private static string ExtractPipeName(string path)
    {
        // Directory.GetFiles returns "\\\\.\\pipe\\<name>"; we only need the
        // <name> piece for NamedPipeClientStream.
        var slash = path.LastIndexOf('\\');
        return slash >= 0 ? path[(slash + 1)..] : path;
    }
}
