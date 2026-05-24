using MindAttic.Terminal.Services;
using NUnit.Framework;

namespace MindAttic.Terminal.Tests;

[TestFixture]
public sealed class RemoteControlBroadcasterTests
{
    [Test]
    public async Task Filters_pipes_by_provider_prefix_and_writes_payload()
    {
        // Two listening "tabs": one Claude, one Codex. Broadcaster should only
        // deliver to the Claude tab so a Codex session isn't disrupted when the
        // user hits Remote Control.
        var claudePipe = $"mindattic-host-claude-{Guid.NewGuid():N}";
        var codexPipe  = $"mindattic-host-codex-{Guid.NewGuid():N}";

        using var claude = new RecordingPipeServer(claudePipe);
        using var codex  = new RecordingPipeServer(codexPipe);

        IEnumerable<string> Enumerator() =>
        [
            $@"\\.\pipe\{claudePipe}",
            $@"\\.\pipe\{codexPipe}",
            @"\\.\pipe\unrelated-noise"
        ];

        var broadcaster = new RemoteControlBroadcaster(Enumerator);
        var result = await broadcaster.BroadcastAsync("Claude", "/remote-control\n");

        Assert.That(result.Delivered, Is.EqualTo(1));
        Assert.That(result.Failed, Is.Empty);
        Assert.That(await claude.Received(), Is.EqualTo("/remote-control\n"));
        Assert.That(codex.WasContacted, Is.False);
    }

    [Test]
    public async Task Reports_zero_delivered_when_no_pipes_match()
    {
        IEnumerable<string> Enumerator() =>
        [
            @"\\.\pipe\mindattic-host-codex-1",
            @"\\.\pipe\something-else"
        ];

        var broadcaster = new RemoteControlBroadcaster(Enumerator);
        var result = await broadcaster.BroadcastAsync("Claude", "/remote-control\n");

        Assert.That(result.Delivered, Is.EqualTo(0));
        Assert.That(result.Failed, Is.Empty);
    }

    private sealed class RecordingPipeServer : IDisposable
    {
        private readonly HostInputPipeServer server;
        private readonly TaskCompletionSource<string> received = new();

        public bool WasContacted { get; private set; }

        public RecordingPipeServer(string pipeName)
        {
            server = new HostInputPipeServer(pipeName, text =>
            {
                WasContacted = true;
                received.TrySetResult(text);
            });
        }

        public async Task<string> Received()
        {
            var winner = await Task.WhenAny(received.Task, Task.Delay(3000));
            return winner == received.Task ? received.Task.Result : "<timeout>";
        }

        public void Dispose() => server.Dispose();
    }
}
