using System.IO;
using System.Linq;
using MindAttic.Launcher.Models;
using MindAttic.Launcher.Services;
using NUnit.Framework;

namespace MindAttic.Launcher.Tests;

/// <summary>An unscoped Launcher asks which account before it opens a Claude tab (M.LNCH-T65), opens the tab in that
/// person's window with their badge and color (M.LNCH-T55), and passes the choice to host as --config-dir so the tab never
/// asks again (M.LNCH-T53).</summary>
[TestFixture]
public sealed class ClaudeTabLoginTests
{
    private string root = "";
    private string accountsRoot = "";

    private static readonly AgentProvider Claude = new() { Key = "Claude", Name = "Claude Code", RunCommand = "claude" };
    private static readonly AgentProvider Codex = new() { Key = "Codex", Name = "OpenAI Codex", RunCommand = "codex" };
    private static readonly Project Repo = new() { Name = "Repo", Path = @"D:\Projects\Repo" };

    [SetUp]
    public void SetUp()
    {
        root = Path.Combine(Path.GetTempPath(), "launcher-tablogin-" + Path.GetRandomFileName());
        accountsRoot = Path.Combine(root, ".claude-accounts");
        Directory.CreateDirectory(accountsRoot);
        ClaudeAccountCatalog.OrchestratorOverride = _ => null;
        ClaudeAccountCatalog.GlobalEmailOverride = () => "ryan@mindattic.com";
        ClaudeAccountCatalog.BusyOverride = _ => false;
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        ClaudeAccountCatalog.OrchestratorOverride = null;
        ClaudeAccountCatalog.GlobalEmailOverride = null;
        ClaudeAccountCatalog.BusyOverride = null;
    }

    private string Login(string email)
    {
        var dir = Path.Combine(accountsRoot, email);
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Test]
    public void More_than_one_login_asks_with_the_same_list_and_auto_preselected()
    {
        var jordan = Login("jordan@mindattic.com");
        ClaudeAccountCatalog.OrchestratorOverride = args => args.Contains("pick")
            ? $$"""{ "configDir": "{{jordan.Replace("\\", "\\\\")}}" }"""
            : null;
        IReadOnlyList<ClaudeAccountOption>? asked = null;
        ClaudeAccountOption? preselected = null;

        var choice = ClaudeTabLogin.Choose(null, Claude, (options, auto) => { asked = options; preselected = auto; return options[0]; }, accountsRoot);

        Assert.That(asked!.Select(o => o.ConfigDir), Is.EqualTo(new[] { "", jordan }));
        Assert.That(preselected!.ConfigDir, Is.EqualTo(jordan));
        Assert.That(choice, Is.EqualTo(new ClaudeTabLogin.Choice(false, "")));
    }

    [Test]
    public void Backing_out_of_the_question_cancels_the_tab()
    {
        Login("jordan@mindattic.com");

        var choice = ClaudeTabLogin.Choose(null, Claude, (_, _) => null, accountsRoot);

        Assert.That(choice.Cancelled, Is.True);
    }

    [Test]
    public void A_single_login_a_scoped_launcher_and_other_providers_skip_the_question()
    {
        ClaudeAccountOption? Fail(IReadOnlyList<ClaudeAccountOption> o, ClaudeAccountOption a) { Assert.Fail("asked"); return null; }

        Assert.That(ClaudeTabLogin.Choose(null, Claude, Fail, accountsRoot), Is.EqualTo(ClaudeTabLogin.Choice.NotAsked));
        Login("jordan@mindattic.com");
        var scope = new AccountScope("jordan@mindattic.com", Path.Combine(accountsRoot, "jordan@mindattic.com"), "Jordan");
        Assert.That(ClaudeTabLogin.Choose(scope, Claude, Fail, accountsRoot), Is.EqualTo(ClaudeTabLogin.Choice.NotAsked));
        Assert.That(ClaudeTabLogin.Choose(null, Codex, Fail, accountsRoot), Is.EqualTo(ClaudeTabLogin.Choice.NotAsked));
    }

    [Test]
    public void A_chosen_login_opens_in_its_window_with_its_badge_and_color_and_tells_host()
    {
        var jordan = Login("jordan@mindattic.com");
        var wt = new WindowsTerminalLauncher(claudeLook: () => null);

        foreach (var tab in new[]
                 {
                     wt.BuildAgentTab(Repo, Claude, "mindattic.exe", "go", login: jordan),
                     wt.BuildAgentTabAtPath("General [Claude]", @"D:\Projects", Claude, "mindattic.exe", prompt: "go", login: jordan),
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

        var tab = wt.BuildAgentTab(Repo, Claude, "mindattic.exe", login: "");

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
        Assert.That(ClaudeLogin.FromOption(jordan, Path.Combine(root, ".claude")), Is.EqualTo(jordan));
        Assert.That(ClaudeLogin.FromOption(Path.Combine(root, ".claude") + "\\", Path.Combine(root, ".claude")), Is.EqualTo(""));
    }

    [Test]
    public void A_scope_wins_over_a_chosen_login_and_other_providers_ignore_it()
    {
        var scope = new AccountScope("amy@mindattic.com", Login("amy@mindattic.com"), "Amy");
        var scoped = new WindowsTerminalLauncher(scope).BuildAgentTab(Repo, Claude, "mindattic.exe", login: Login("jordan@mindattic.com"));
        Assert.That(scoped.Command[scoped.Command.ToList().IndexOf("--config-dir") + 1], Is.EqualTo(scope.ConfigDir));

        var codex = new WindowsTerminalLauncher(claudeLook: () => null).BuildAgentTab(Repo, Codex, "mindattic.exe", login: "");
        Assert.That(codex.Command, Does.Not.Contain("--config-dir"));
        Assert.That(codex.Window, Is.Null);
    }
}
