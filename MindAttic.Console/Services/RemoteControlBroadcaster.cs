using System.IO.Pipes;
using System.Text;

namespace MindAttic.Console.Services;

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

        // Fan out: each connect is IO-bound with a 1s timeout, so a serial
        // foreach makes a 5-stale-tab broadcast take 5 seconds. Run them in
        // parallel — order doesn't matter, only the delivered/failed totals.
        var bytes = Encoding.UTF8.GetBytes(payload);
        var sends = targets.Select(name => SendToAsync(name, bytes, ct));
        var outcomes = await Task.WhenAll(sends).ConfigureAwait(false);

        var delivered = outcomes.Count(o => o.Ok);
        var failed = outcomes.Where(o => !o.Ok).Select(o => o.Name).ToList();
        return new Result { Delivered = delivered, Failed = failed };
    }

    private static async Task<(string Name, bool Ok)> SendToAsync(string name, byte[] bytes, CancellationToken ct)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", name, PipeDirection.Out, PipeOptions.Asynchronous);
            await client.ConnectAsync(1000, ct).ConfigureAwait(false);
            await client.WriteAsync(bytes, ct).ConfigureAwait(false);
            await client.FlushAsync(ct).ConfigureAwait(false);
            return (name, true);
        }
        catch
        {
            return (name, false);
        }
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
