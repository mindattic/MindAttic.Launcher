using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using MindAttic.Launcher.Services;
using NUnit.Framework;

namespace MindAttic.Launcher.Tests;

[TestFixture]
public sealed class HostInputPipeServerTests
{
    private static string NewPipe() => $"mindattic-host-test-{Guid.NewGuid():N}";

    private static async Task Send(string pipe, string text)
    {
        using var client = new NamedPipeClientStream(".", pipe, PipeDirection.Out);
        await client.ConnectAsync(5000);
        if (text.Length > 0)
        {
            var bytes = new UTF8Encoding(false).GetBytes(text);
            await client.WriteAsync(bytes);
            await client.FlushAsync();
        }
    }

    private static async Task<string> Next(BlockingCollection<string> q)
    {
        var task = Task.Run(() => q.TryTake(out var s, 5000) ? s : "<timeout>");
        return await task;
    }

    [Test]
    public void PipeName_lowercases_provider_and_appends_pid()
    {
        Assert.That(HostInputPipeServer.PipeName("ClAuDe", 1234), Is.EqualTo("mindattic-host-claude-1234"));
        Assert.That(HostInputPipeServer.PipeName("CODEX", 7), Does.StartWith(HostInputPipeServer.PipeNamePrefix));
    }

    [Test]
    public async Task Empty_payload_is_not_forwarded()
    {
        var q = new BlockingCollection<string>();
        var pipe = NewPipe();
        using var server = new HostInputPipeServer(pipe, q.Add);

        await Send(pipe, "");
        await Send(pipe, "after");

        Assert.That(await Next(q), Is.EqualTo("after"));
        Assert.That(q.Count, Is.EqualTo(0));
    }

    [Test]
    public async Task Sequential_connections_are_each_delivered()
    {
        var q = new BlockingCollection<string>();
        var pipe = NewPipe();
        using var server = new HostInputPipeServer(pipe, q.Add);

        foreach (var msg in new[] { "one", "two", "three" })
        {
            await Send(pipe, msg);
            Assert.That(await Next(q), Is.EqualTo(msg));
        }
    }

    [Test]
    public async Task Utf8_non_ascii_text_survives()
    {
        var q = new BlockingCollection<string>();
        var pipe = NewPipe();
        using var server = new HostInputPipeServer(pipe, q.Add);

        const string text = "héllo wörld — 日本語 🙂";
        await Send(pipe, text);

        Assert.That(await Next(q), Is.EqualTo(text));
    }

    [Test]
    public async Task Dispose_returns_promptly_while_waiting_for_a_connection()
    {
        var pipe = NewPipe();
        var server = new HostInputPipeServer(pipe, _ => { });
        await Task.Delay(200);

        var sw = Stopwatch.StartNew();
        server.Dispose();
        sw.Stop();

        Assert.That(sw.ElapsedMilliseconds, Is.LessThan(1000));
    }
}
