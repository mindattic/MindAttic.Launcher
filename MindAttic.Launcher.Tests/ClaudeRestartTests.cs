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

    [Test]
    public void Parse_reads_a_login_move_with_billing()
    {
        var r = HostControlPipeServer.Parse("""{"type":"restart-resume","billing":"team","configDir":"C:\\Users\\me\\.claude-accounts\\a@b.com","session":"abc","cliPid":7}""");

        Assert.That(r, Is.EqualTo(new RestartResumeRequest("team", "abc", 7, null, @"C:\Users\me\.claude-accounts\a@b.com")));
    }

    [Test]
    public void Parse_allows_a_login_move_without_billing()
    {
        var r = HostControlPipeServer.Parse("""{"type":"restart-resume","configDir":"","session":"abc","cliPid":7}""");

        Assert.That(r, Is.EqualTo(new RestartResumeRequest(null, "abc", 7, null, "")));
    }

    [Test]
    public void Parse_without_configDir_keeps_the_current_login() =>
        Assert.That(HostControlPipeServer.Parse("""{"type":"restart-resume","billing":"api","session":"abc","cliPid":7}""")!.ConfigDir, Is.Null);

    [TestCase("""{"type":"restart-resume","session":"abc","cliPid":1}""")]
    [TestCase("""{"type":"restart-resume","configDir":5,"session":"abc","cliPid":1}""")]
    [TestCase("""{"type":"restart-resume","billing":"bitcoin","configDir":"","session":"abc","cliPid":1}""")]
    [TestCase("""{"type":"something-new","billing":"api","session":"abc","cliPid":1}""")]
    [TestCase("""{"type":"restart-resume","billing":"bitcoin","session":"abc","cliPid":1}""")]
    [TestCase("""{"type":"restart-resume","billing":"team","cliPid":1}""")]
    [TestCase("""{"type":"restart-resume","billing":"team","session":"abc","cliPid":"1"}""")]
    [TestCase("not json")]
    [TestCase("")]
    public void Parse_ignores_anything_but_a_well_formed_restart_resume(string text) =>
        Assert.That(HostControlPipeServer.Parse(text), Is.Null);

    [Test]
    public void Parse_reads_the_orchestrators_failover_request()
    {
        // The exact line MindAttic.Orchestrator's AccountMove.FailoverMessage sends (M.ORCH-T664).
        var r = HostControlPipeServer.Parse("""{"type":"restart-resume","reason":"failover","billing":"api","configDir":"","now":true,"session":"abc","cliPid":4242,"cwd":"D:\\Projects\\Prose","requestedAt":"2026-10-23T16:00:00Z"}""");

        Assert.That(r, Is.EqualTo(new RestartResumeRequest("api", "abc", 4242, @"D:\Projects\Prose", "", Failover: true, Now: true)));
    }

    [Test]
    public void Parse_reads_a_move_back_off_the_key_as_not_now() =>
        Assert.That(HostControlPipeServer.Parse("""{"type":"restart-resume","reason":"failover","billing":"team","configDir":"C:\\a","now":false,"session":"abc","cliPid":7}"""),
            Is.EqualTo(new RestartResumeRequest("team", "abc", 7, null, @"C:\a", Failover: true, Now: false)));

    [Test]
    public void Parse_needs_a_billing_on_a_failover() =>
        Assert.That(HostControlPipeServer.Parse("""{"type":"restart-resume","reason":"failover","configDir":"","now":true,"session":"abc","cliPid":7}"""), Is.Null);

    [Test]
    public void Parse_reads_a_relocate_request_with_neither_billing_nor_configDir()
    {
        var r = HostControlPipeServer.Parse("""{"type":"restart-resume","reason":"relocate","session":"abc","cliPid":123,"cwd":"D:\\Projects\\Prose","requestedAt":"2026-10-25T12:00:00Z"}""");

        Assert.That(r, Is.EqualTo(new RestartResumeRequest(null, "abc", 123, @"D:\Projects\Prose", Relocate: true)));
    }

    [Test]
    public void Parse_still_rejects_a_request_with_none_of_billing_configDir_or_relocate() =>
        Assert.That(HostControlPipeServer.Parse("""{"type":"restart-resume","session":"abc","cliPid":123}"""), Is.Null);

    [Test]
    public void A_failover_is_never_dropped_for_differing_from_the_mode()
    {
        var billing = Path.Combine(dir, "billing.json");
        File.WriteAllText(billing, """{"mode": "team"}""");

        Assert.That(ClaudeRestartGate.StillWanted(Request("api") with { Failover = true }, billing), Is.True);
        Assert.That(ClaudeRestartGate.StillWanted(Request("api") with { Failover = true }, Path.Combine(dir, "missing.json")), Is.True);
    }

    [Test]
    public void A_now_request_restarts_without_waiting_for_the_idle_point()
    {
        State("busy");

        Assert.That(ClaudeRestartGate.IsReady(Request("api") with { Failover = true, Now = true }, dir), Is.True);
        Assert.That(ClaudeRestartGate.IsReady(Request("api") with { Failover = true }, dir), Is.False);
        State("idle");
        Assert.That(ClaudeRestartGate.IsReady(Request("api") with { Failover = true }, dir), Is.True);
    }

    [Test]
    public void A_failovers_billing_holds_until_another_request_says_otherwise()
    {
        string? billing = null;

        billing = ClaudeBillingMode.Next(billing, Request("api") with { Failover = true, ConfigDir = "" });
        Assert.That(billing, Is.EqualTo(ClaudeBillingMode.Api));

        // A later login move (no billing) keeps it.
        billing = ClaudeBillingMode.Next(billing, new RestartResumeRequest(null, "s", 1, null, @"C:\a"));
        Assert.That(billing, Is.EqualTo(ClaudeBillingMode.Api));

        // The move back off the key is its own billing too.
        billing = ClaudeBillingMode.Next(billing, Request("team") with { Failover = true });
        Assert.That(billing, Is.EqualTo(ClaudeBillingMode.Team));

        // A billing-switch request puts the CLI back on the mode.
        billing = ClaudeBillingMode.Next(billing, Request("api"));
        Assert.That(billing, Is.Null);
    }

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
    public void A_login_move_alone_does_not_wait_on_the_billing_mode() =>
        Assert.That(ClaudeRestartGate.StillWanted(new RestartResumeRequest(null, "s", 1, null, ""), Path.Combine(dir, "missing.json")), Is.True);

    [Test]
    public void The_restart_environment_follows_the_requested_login()
    {
        var account = @"C:\Users\me\.claude-accounts\a@b.com";
        var psi = new System.Diagnostics.ProcessStartInfo("claude");

        // A path sets CLAUDE_CONFIG_DIR, and the host remembers it for later restarts.
        var current = ClaudeLogin.Next(ClaudeLogin.Initial(null), Request() with { ConfigDir = account });
        ClaudeLogin.Apply(psi, current);
        Assert.That(psi.EnvironmentVariables[ClaudeLogin.ConfigDirVariable], Is.EqualTo(account));

        // A later billing-only restart (no configDir) keeps the moved login.
        current = ClaudeLogin.Next(current, Request("api"));
        Assert.That(current, Is.EqualTo(account));

        // "" is the global login: CLAUDE_CONFIG_DIR is removed.
        current = ClaudeLogin.Next(current, Request() with { ConfigDir = "" });
        ClaudeLogin.Apply(psi, current);
        Assert.That(psi.EnvironmentVariables.ContainsKey(ClaudeLogin.ConfigDirVariable), Is.False);
    }

    [Test]
    public void The_host_starts_on_the_login_it_inherited()
    {
        Assert.That(ClaudeLogin.Initial(null), Is.EqualTo(""));
        Assert.That(ClaudeLogin.Initial(@" D:\acct "), Is.EqualTo(@"D:\acct"));
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
