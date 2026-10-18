using System.IO.Pipes;
using System.Text;
using MindAttic.Launcher.Services;
using NUnit.Framework;

namespace MindAttic.Launcher.Tests;

/// <summary>The Orchestrator's restart-resume requests (M.LNCH-T38): the control pipe, and when a hosted
/// Claude CLI may be restarted for one.</summary>
[TestFixture]
public sealed class ClaudeRestartTests
{
    private string dir = "";

    [SetUp]
    public void SetUp()
    {
        dir = Path.Combine(Path.GetTempPath(), "MindAttic.Launcher.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
    }

    private static RestartResumeRequest Request(string billing = "team", int pid = 4242) =>
        new(billing, "11111111-2222-3333-4444-555555555555", pid, @"D:\Projects\Prose");

    private void State(string state) =>
        File.WriteAllText(Path.Combine(dir, Request().Session + ".state.json"), $$"""{"auth": "api-key", "pid": 4242, "state": "{{state}}"}""");

    [Test]
    public void Parse_reads_a_restart_resume_line()
    {
        var r = HostControlPipeServer.Parse("""{"type":"restart-resume","reason":"billing","billing":"api","session":"abc","cliPid":4242,"cwd":"D:\\Projects\\Prose","requestedAt":"2026-10-18T16:00:00Z"}""" + "\n");

        Assert.That(r, Is.EqualTo(new RestartResumeRequest("api", "abc", 4242, @"D:\Projects\Prose")));
    }

    [TestCase("""{"type":"something-new","billing":"api","session":"abc","cliPid":1}""")]
    [TestCase("""{"type":"restart-resume","billing":"bitcoin","session":"abc","cliPid":1}""")]
    [TestCase("""{"type":"restart-resume","billing":"team","cliPid":1}""")]
    [TestCase("""{"type":"restart-resume","billing":"team","session":"abc","cliPid":"1"}""")]
    [TestCase("not json")]
    [TestCase("")]
    public void Parse_ignores_anything_but_a_well_formed_restart_resume(string text) =>
        Assert.That(HostControlPipeServer.Parse(text), Is.Null);

    [Test]
    public void The_control_pipe_is_outside_the_tab_prefix() =>
        Assert.That(HostControlPipeServer.PipeName("Claude", 77), Is.EqualTo("mindattic-control-claude-77")
            .And.Not.StartWith(HostInputPipeServer.PipeNamePrefix));

    [Test]
    public void The_control_pipe_hands_a_request_to_the_sink()
    {
        var name = $"mindattic-control-test-{Guid.NewGuid():N}";
        var got = new TaskCompletionSource<RestartResumeRequest>();
        using var server = new HostControlPipeServer(name, r => got.TrySetResult(r));

        using (var client = new NamedPipeClientStream(".", name, PipeDirection.Out))
        {
            client.Connect(5000);
            var bytes = Encoding.UTF8.GetBytes("""{"type":"restart-resume","reason":"billing","billing":"team","session":"s1","cliPid":9,"cwd":"C:\\x"}""" + "\n");
            client.Write(bytes);
        }

        Assert.That(got.Task.Wait(5000), Is.True);
        Assert.That(got.Task.Result, Is.EqualTo(new RestartResumeRequest("team", "s1", 9, @"C:\x")));
    }

    [Test]
    public void A_request_is_only_for_the_CLI_it_names()
    {
        Assert.That(ClaudeRestartGate.IsFor(Request(pid: 4242), 4242), Is.True);
        Assert.That(ClaudeRestartGate.IsFor(Request(pid: 4242), 5555), Is.False);
    }

    [Test]
    public void A_request_is_dropped_once_the_mode_has_switched_back()
    {
        var billing = Path.Combine(dir, "billing.json");
        File.WriteAllText(billing, """{"mode": "team"}""");

        Assert.That(ClaudeRestartGate.StillWanted(Request("team"), billing), Is.True);
        Assert.That(ClaudeRestartGate.StillWanted(Request("api"), billing), Is.False);
        Assert.That(ClaudeRestartGate.StillWanted(Request("team"), Path.Combine(dir, "missing.json")), Is.False);
    }

    [Test]
    public void A_busy_CLI_is_not_restarted()
    {
        State("busy");

        Assert.That(ClaudeRestartGate.IsIdle(Request(), dir), Is.False);
    }

    [Test]
    public void An_idle_CLI_with_no_subagents_may_restart()
    {
        State("idle");

        Assert.That(ClaudeRestartGate.IsIdle(Request(), dir), Is.True);
    }

    [Test]
    public void A_CLI_with_no_state_file_is_never_taken_as_idle() =>
        Assert.That(ClaudeRestartGate.IsIdle(Request(), dir), Is.False);

    [Test]
    public void An_idle_CLI_waits_while_a_subagent_of_it_runs()
    {
        State("idle");
        Directory.CreateDirectory(Path.Combine(dir, "subagents"));
        var mark = Path.Combine(dir, "subagents", "a8fa8a27dc8fe6405");
        File.WriteAllText(mark, "4242");

        Assert.That(ClaudeRestartGate.IsIdle(Request(), dir), Is.False);

        // Another CLI's subagent doesn't hold it, and neither does a long-dead leftover mark.
        File.WriteAllText(mark, "5555");
        Assert.That(ClaudeRestartGate.IsIdle(Request(), dir), Is.True);
        File.WriteAllText(mark, "4242");
        File.SetLastWriteTimeUtc(mark, DateTime.UtcNow - ClaudeRestartGate.StaleSubagentMark - TimeSpan.FromMinutes(1));
        Assert.That(ClaudeRestartGate.IsIdle(Request(), dir), Is.True);
    }

    [Test]
    public void A_session_id_that_isnt_a_file_name_is_never_idle()
    {
        Assert.That(ClaudeRestartGate.IsIdle(Request() with { Session = "..\\..\\x" }, dir), Is.False);
    }
}
