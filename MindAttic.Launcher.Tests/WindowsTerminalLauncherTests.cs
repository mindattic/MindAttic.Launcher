using System.IO;
using System.Linq;
using MindAttic.Launcher.Models;
using MindAttic.Launcher.Services;
using NUnit.Framework;

namespace MindAttic.Launcher.Tests;

[TestFixture]
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
}
