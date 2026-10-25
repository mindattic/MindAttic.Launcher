using System.IO;
using System.Linq;
using MindAttic.Launcher.Models;
using MindAttic.Launcher.Services;
using NUnit.Framework;

namespace MindAttic.Launcher.Tests;

[TestFixture]
[NonParallelizable] // several tests set MINDATTIC_ORCHESTRATOR_TERMINALS, which is process-wide
public sealed class WindowsTerminalLauncherTests
{
    private static readonly Project Project = new() { Name = "Workspace", Path = @"D:\Projects\MindAttic" };
    private static readonly AgentProvider Provider = new() { Key = "Claude", Name = "Claude Code", RunCommand = "claude" };

    [Test]
    public void BuildAgentTab_appends_prompt_as_last_host_arg_when_provided()
    {
        var tab = new WindowsTerminalLauncher().BuildAgentTab(Project, Provider, "mindattic.exe", "tidy every repo");

        Assert.That(tab.Command, Does.Contain("--prompt"));
        // The order text must be the final argument so the host forwards it as
        // the agent's positional seed prompt.
        Assert.That(tab.Command[^2], Is.EqualTo("--prompt"));
        Assert.That(tab.Command[^1], Is.EqualTo("tidy every repo"));
    }

    [Test]
    public void BuildAgentTab_omits_prompt_when_null_or_blank()
    {
        var none  = new WindowsTerminalLauncher().BuildAgentTab(Project, Provider, "mindattic.exe");
        var blank = new WindowsTerminalLauncher().BuildAgentTab(Project, Provider, "mindattic.exe", "   ");

        Assert.That(none.Command, Does.Not.Contain("--prompt"));
        Assert.That(blank.Command, Does.Not.Contain("--prompt"));
    }

    [Test]
    public void BuildAgentTabAtPath_hosts_by_path_not_name()
    {
        var tab = new WindowsTerminalLauncher()
            .BuildAgentTabAtPath("Status [Claude]", @"D:\Projects\MindAttic", Provider, "mindattic.exe");

        // Path mode roots the host at a directory with no roster entry — it must
        // pass --path (never --name).
        Assert.That(tab.Command, Does.Contain("--path"));
        Assert.That(tab.Command, Does.Not.Contain("--name"));
        var pathIdx = tab.Command.ToList().IndexOf("--path");
        Assert.That(tab.Command[pathIdx + 1], Is.EqualTo(@"D:\Projects\MindAttic"));
        Assert.That(tab.WorkingDirectory, Is.EqualTo(@"D:\Projects\MindAttic"));
    }

    [Test]
    public void BuildAgentTabAtPath_appends_prompt_as_last_host_arg_when_provided()
    {
        var tab = new WindowsTerminalLauncher()
            .BuildAgentTabAtPath("Status [Claude]", @"D:\Projects\MindAttic", Provider, "mindattic.exe",
                prompt: "tidy every repo");

        Assert.That(tab.Command[^2], Is.EqualTo("--prompt"));
        Assert.That(tab.Command[^1], Is.EqualTo("tidy every repo"));
    }

    [Test]
    public void BuildAgentTabAtPath_omits_prompt_when_null_or_blank()
    {
        var none  = new WindowsTerminalLauncher()
            .BuildAgentTabAtPath("Status", @"D:\Projects\MindAttic", Provider, "mindattic.exe");
        var blank = new WindowsTerminalLauncher()
            .BuildAgentTabAtPath("Status", @"D:\Projects\MindAttic", Provider, "mindattic.exe", prompt: "  ");

        Assert.That(none.Command, Does.Not.Contain("--prompt"));
        Assert.That(blank.Command, Does.Not.Contain("--prompt"));
    }

    // ---- an explicit login (outside the usual scope) ----------------------------------

    private string loginRoot = "";

    [SetUp]
    public void SetUpLoginRoot() => loginRoot = Path.Combine(Path.GetTempPath(), "launcher-wtl-" + Path.GetRandomFileName());

    [TearDown]
    public void TearDownLoginRoot()
    {
        try { Directory.Delete(loginRoot, recursive: true); } catch (IOException) { }
    }

    private string Login(string email)
    {
        var dir = Path.Combine(loginRoot, email);
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Test]
    public void An_explicit_login_opens_in_its_window_with_its_badge_and_color_and_tells_host()
    {
        var jordan = Login("jordan@mindattic.com");
        var wt = new WindowsTerminalLauncher(claudeLook: () => null);

        foreach (var tab in new[]
                 {
                     wt.BuildAgentTab(Project, Provider, "mindattic.exe", "go", login: jordan),
                     wt.BuildAgentTabAtPath("General [Claude]", @"D:\Projects", Provider, "mindattic.exe", prompt: "go", login: jordan),
                 })
        {
            var look = LoginLook.ForConfigDir(jordan);
            var args = tab.Command.ToList();
            Assert.That(args[args.IndexOf("--config-dir") + 1], Is.EqualTo(jordan));
            Assert.That(tab.Window, Is.EqualTo("jordan"));
            Assert.That(tab.Title, Does.StartWith("JO: "));
            Assert.That(tab.TabColor, Is.EqualTo(look.TabColor));
            Assert.That(args[^2], Is.EqualTo("--prompt"), "the prompt stays the host's last argument");
        }
    }

    [Test]
    public void The_global_login_is_passed_as_its_own_folder_which_host_maps_back_to_global()
    {
        var global = new LoginLook("ryan", "RY", "#2E7D32");
        var wt = new WindowsTerminalLauncher(claudeLook: () => null, loginLook: dir => dir == "" ? global : LoginLook.ForConfigDir(dir));

        var tab = wt.BuildAgentTab(Project, Provider, "mindattic.exe", login: "");

        var args = tab.Command.ToList();
        var passed = args[args.IndexOf("--config-dir") + 1];
        Assert.That(passed, Is.EqualTo(ClaudeResume.GlobalConfigDir));
        Assert.That(ClaudeLogin.FromOption(passed), Is.EqualTo(""));
        Assert.That(tab.Window, Is.EqualTo("ryan"));
    }

    [Test]
    public void FromOption_keeps_a_per_cli_folder()
    {
        var jordan = Login("jordan@mindattic.com");
        Assert.That(ClaudeLogin.FromOption(jordan, Path.Combine(loginRoot, ".claude")), Is.EqualTo(jordan));
        Assert.That(ClaudeLogin.FromOption(Path.Combine(loginRoot, ".claude") + "\\", Path.Combine(loginRoot, ".claude")), Is.EqualTo(""));
    }

    [Test]
    public void A_scope_wins_over_an_explicit_login_and_other_providers_ignore_it()
    {
        var codexProvider = new AgentProvider { Key = "Codex", Name = "OpenAI Codex", RunCommand = "codex" };
        var scope = new AccountScope("amy@mindattic.com", Login("amy@mindattic.com"), "Amy");
        var scoped = new WindowsTerminalLauncher(scope).BuildAgentTab(Project, Provider, "mindattic.exe", login: Login("jordan@mindattic.com"));
        Assert.That(scoped.Command[scoped.Command.ToList().IndexOf("--config-dir") + 1], Is.EqualTo(scope.ConfigDir));

        var codex = new WindowsTerminalLauncher(claudeLook: () => null).BuildAgentTab(Project, codexProvider, "mindattic.exe", login: "");
        Assert.That(codex.Command, Does.Not.Contain("--config-dir"));
        Assert.That(codex.Window, Is.Null);
    }

    // ---- routing an agent-host tab into the Orchestrator's embedded Terminals panel (M.LNCH-T77) -----------------

    private static void WithOrchestratorPipe(string? pipeName, Action run)
    {
        var old = Environment.GetEnvironmentVariable(WindowsTerminalLauncher.OrchestratorTerminalsEnvVar);
        try
        {
            Environment.SetEnvironmentVariable(WindowsTerminalLauncher.OrchestratorTerminalsEnvVar, pipeName);
            run();
        }
        finally { Environment.SetEnvironmentVariable(WindowsTerminalLauncher.OrchestratorTerminalsEnvVar, old); }
    }

    [Test]
    public void Outside_the_Orchestrator_an_agent_host_tab_still_just_opens_wt()
    {
        WithOrchestratorPipe(null, () =>
        {
            WindowsTerminalLauncher.Tab? launched = null;
            var wt = new WindowsTerminalLauncher(
                sendToOrchestrator: (_, _) => throw new InvalidOperationException("must not touch the pipe when unembedded"),
                launchWindowsTerminal: t => launched = t);

            wt.Open(wt.BuildAgentTab(Project, Provider, "mindattic.exe"));

            Assert.That(launched, Is.Not.Null);
            Assert.That(launched!.Command, Does.Contain("--name"));
        });
    }

    [Test]
    public void A_utility_tab_always_opens_wt_even_when_embedded()
    {
        WithOrchestratorPipe("launcher-test-does-not-exist", () =>
        {
            WindowsTerminalLauncher.Tab? launched = null;
            var wt = new WindowsTerminalLauncher(
                sendToOrchestrator: (_, _) => throw new InvalidOperationException("a non-agent-host tab must not touch the pipe"),
                launchWindowsTerminal: t => launched = t);

            // BuildCmdTab/BuildPowerShellTab (OpenElevated's callers) never set IsAgentHost.
            wt.Open(wt.BuildCmdTab(@"D:\Projects\MindAttic"));

            Assert.That(launched, Is.Not.Null);
        });
    }

    [Test]
    public void Embedded_agent_host_tab_sends_the_pipe_request_and_never_opens_wt()
    {
        WithOrchestratorPipe("launcher-test-pipe", () =>
        {
            string? pipeNameSeen = null;
            string? jsonSeen = null;
            // No login known (claudeLook: () => null) so configDir/email/initials/tabColor all come back
            // unset — the request should omit them rather than send them as null.
            var wt = new WindowsTerminalLauncher(
                claudeLook: () => null,
                sendToOrchestrator: (pipe, json) => { pipeNameSeen = pipe; jsonSeen = json; return ""; },
                launchWindowsTerminal: _ => Assert.Fail("a successful pipe send must not also open wt"));

            var tab = wt.BuildAgentTab(Project, Provider, "mindattic.exe", "go");
            wt.Open(tab);

            Assert.That(pipeNameSeen, Is.EqualTo("launcher-test-pipe"));
            using var doc = System.Text.Json.JsonDocument.Parse(jsonSeen!);
            var root = doc.RootElement;
            Assert.That(root.GetProperty("title").GetString(), Is.EqualTo(tab.Title));
            Assert.That(root.GetProperty("directory").GetString(), Is.EqualTo(Project.Path));
            Assert.That(root.GetProperty("repo").GetString(), Is.EqualTo(Project.Name));
            Assert.That(root.GetProperty("provider").GetString(), Is.EqualTo("Claude"));
            var command = root.GetProperty("command").EnumerateArray().Select(e => e.GetString()).ToList();
            Assert.That(command, Is.EqualTo(tab.Command));
            Assert.That(root.TryGetProperty("configDir", out _), Is.False);
            Assert.That(root.TryGetProperty("email", out _), Is.False);
            Assert.That(root.TryGetProperty("initials", out _), Is.False);
            Assert.That(root.TryGetProperty("tabColor", out _), Is.False);
        });
    }

    [Test]
    public void Embedded_agent_host_tab_reports_the_dashboards_error_and_does_not_also_open_wt()
    {
        WithOrchestratorPipe("launcher-test-pipe-error", () =>
        {
            var wt = new WindowsTerminalLauncher(
                sendToOrchestrator: (_, _) => "no such project directory",
                launchWindowsTerminal: _ => Assert.Fail("an errored pipe send must not also open wt (duplicate)"));

            var ex = Assert.Throws<InvalidOperationException>(() => wt.Open(wt.BuildAgentTab(Project, Provider, "mindattic.exe")));
            Assert.That(ex!.Message, Does.Contain("no such project directory"));
        });
    }

    [Test]
    public void Nothing_listening_on_the_pipe_falls_back_to_wt_without_duplicating()
    {
        WithOrchestratorPipe("launcher-test-pipe-gone", () =>
        {
            WindowsTerminalLauncher.Tab? launched = null;
            var wt = new WindowsTerminalLauncher(
                sendToOrchestrator: (_, _) => null, // nothing listening (e.g. the dashboard closed)
                launchWindowsTerminal: t => launched = t);

            wt.Open(wt.BuildAgentTab(Project, Provider, "mindattic.exe"));

            Assert.That(launched, Is.Not.Null);
        });
    }

    [Test]
    public void A_real_pipe_round_trip_carries_the_login_and_a_blank_reply_means_success()
    {
        var pipeName = "launcher-test-real-pipe-" + Guid.NewGuid().ToString("N");
        var jordan = Login("jordan@mindattic.com");

        using var server = new System.IO.Pipes.NamedPipeServerStream(pipeName, System.IO.Pipes.PipeDirection.InOut, 1,
            System.IO.Pipes.PipeTransmissionMode.Byte, System.IO.Pipes.PipeOptions.Asynchronous);
        var serverTask = Task.Run(() =>
        {
            server.WaitForConnection();
            using var reader = new StreamReader(server, System.Text.Encoding.UTF8, leaveOpen: true);
            using var writer = new StreamWriter(server, new System.Text.UTF8Encoding(false), leaveOpen: true) { NewLine = "\n", AutoFlush = true };
            var line = reader.ReadLine();
            writer.WriteLine(""); // blank reply = opened fine
            return line;
        });

        WithOrchestratorPipe(pipeName, () =>
        {
            var wt = new WindowsTerminalLauncher(
                claudeLook: () => null,
                launchWindowsTerminal: _ => Assert.Fail("a successful real pipe round trip must not also open wt"));

            Assert.DoesNotThrow(() => wt.Open(wt.BuildAgentTab(Project, Provider, "mindattic.exe", login: jordan)));
        });

        Assert.That(serverTask.Wait(TimeSpan.FromSeconds(5)), Is.True, "server never saw the request line");
        using var doc = System.Text.Json.JsonDocument.Parse(serverTask.Result!);
        Assert.That(doc.RootElement.GetProperty("configDir").GetString(), Is.EqualTo(jordan));
        Assert.That(doc.RootElement.GetProperty("email").GetString(), Is.EqualTo("jordan@mindattic.com"));
        Assert.That(doc.RootElement.GetProperty("initials").GetString(), Is.EqualTo("JO"));
    }

    [Test]
    public void A_real_connect_to_nobody_listening_falls_back_to_wt()
    {
        var pipeName = "launcher-test-nobody-home-" + Guid.NewGuid().ToString("N");

        WithOrchestratorPipe(pipeName, () =>
        {
            WindowsTerminalLauncher.Tab? launched = null;
            var wt = new WindowsTerminalLauncher(launchWindowsTerminal: t => launched = t);

            wt.Open(wt.BuildAgentTab(Project, Provider, "mindattic.exe"));

            Assert.That(launched, Is.Not.Null);
        });
    }
}
