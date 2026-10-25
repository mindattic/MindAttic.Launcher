using System.IO;
using System.Linq;
using MindAttic.Launcher.Models;
using MindAttic.Launcher.Services;
using NUnit.Framework;

namespace MindAttic.Launcher.Tests;

/// <summary>A terminal window per person (M.LNCH-T55): a Claude tab goes in its login's window, badged and colored as the
/// login, the same rule as the Orchestrator (M.ORCH-T616).</summary>
[TestFixture]
public sealed class LoginLookTests
{
    private string root = "";

    private static readonly AgentProvider Claude = new() { Key = "Claude", Name = "Claude Code", RunCommand = "claude" };
    private static readonly AgentProvider Codex = new() { Key = "Codex", Name = "OpenAI Codex", RunCommand = "codex" };
    private static readonly Project Repo = new() { Name = "Repo", Path = @"D:\Projects\Repo", TabColor = "#123456" };

    [SetUp]
    public void SetUp()
    {
        root = Path.Combine(Path.GetTempPath(), "launcher-look-" + Path.GetRandomFileName());
        Directory.CreateDirectory(root);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(root, recursive: true); } catch (IOException) { }
    }

    private string Login(string email, string? lookJson = null)
    {
        var dir = Path.Combine(root, ".claude-accounts", email);
        Directory.CreateDirectory(dir);
        if (lookJson is not null) File.WriteAllText(Path.Combine(dir, AccountScope.LookFileName), lookJson);
        return dir;
    }

    [Test]
    public void The_look_file_names_the_window_badge_and_color()
    {
        var dir = Login("pdftransferjr@mindattic.com", """{ "email": "pdftransferjr@mindattic.com", "alias": "Amy Reed", "tabColor": "#a1b2c3" }""");

        var look = LoginLook.ForConfigDir(dir);

        Assert.That((look.Alias, look.Window, look.Initials, look.TabColor), Is.EqualTo(("Amy Reed", "Amy-Reed", "AR", "#A1B2C3")));
    }

    [Test]
    public void Explicit_initials_win_and_one_word_aliases_give_their_first_two_letters()
    {
        Assert.That(LoginLook.ForConfigDir(Login("a@b.com", """{ "alias": "Jordan" }""")).Initials, Is.EqualTo("JO"));
        Assert.That(LoginLook.ForConfigDir(Login("c@b.com", """{ "alias": "Jordan", "initials": "jd" }""")).Initials, Is.EqualTo("JD"));
    }

    [Test]
    public void Without_a_look_file_the_email_names_the_login_and_its_color_is_stable()
    {
        var dir = Login("ryan@mindattic.com");

        var look = LoginLook.ForConfigDir(dir);

        Assert.That((look.Window, look.Initials), Is.EqualTo(("ryan", "RY")));
        Assert.That(look.TabColor, Does.Match("^#[0-9A-F]{6}$"));
        Assert.That(LoginLook.ForConfigDir(dir).TabColor, Is.EqualTo(look.TabColor));
    }

    [Test]
    public void Another_emails_look_file_is_ignored()
    {
        var dir = Login("ryan@mindattic.com", """{ "email": "someone@else.com", "alias": "Someone" }""");

        Assert.That(LoginLook.ForConfigDir(dir).Window, Is.EqualTo("ryan"));
    }

    [Test]
    public void Window_names_wt_would_misread_are_prefixed()
    {
        Assert.That(LoginLook.ForConfigDir(Login("x@b.com", """{ "alias": "new" }""")).Window, Is.EqualTo("claude-new"));
        Assert.That(LoginLook.ForConfigDir(Login("y@b.com", """{ "alias": "42" }""")).Window, Is.EqualTo("claude-42"));
    }

    [Test]
    public void The_global_login_is_read_from_the_home_claude_folder()
    {
        File.WriteAllText(Path.Combine(root, ".claude.json"), """{ "oauthAccount": { "emailAddress": "ryan@mindattic.com" } }""");
        Directory.CreateDirectory(Path.Combine(root, ".claude"));
        File.WriteAllText(Path.Combine(root, ".claude", AccountScope.LookFileName), """{ "email": "ryan@mindattic.com", "alias": "Ryan DeBraal" }""");

        var look = LoginLook.ForConfigDir(null, root);

        Assert.That((look.Window, look.Initials), Is.EqualTo(("Ryan-DeBraal", "RD")));
    }

    [Test]
    public void A_claude_tab_goes_in_its_logins_window_badged_and_colored_as_the_login()
    {
        var look = new LoginLook("Jordan", "JO", "#2E7D32");
        var wt = new WindowsTerminalLauncher(claudeLook: () => look);

        foreach (var tab in new[]
                 {
                     wt.BuildAgentTab(Repo, Claude, "mindattic.exe"),
                     wt.BuildAgentTabAtPath("General [Claude]", @"D:\Projects", Claude, "mindattic.exe", tabColor: "#123456"),
                 })
        {
            Assert.That(tab.Window, Is.EqualTo("Jordan"));
            Assert.That(tab.Title, Does.StartWith("JO: ").And.EndWith(" [Claude]"));
            Assert.That(tab.TabColor, Is.EqualTo("#2E7D32"));
            var args = tab.Command.ToList();
            Assert.That(args[args.IndexOf("--title") + 1], Is.EqualTo(tab.Title), "the host pins the same badged title");

            var wtArgs = WindowsTerminalLauncher.BuildArgList(tab).ToList();
            Assert.That(wtArgs.Take(3), Is.EqualTo(new[] { "-w", "Jordan", "new-tab" }));
            Assert.That(wtArgs[wtArgs.IndexOf("--tabColor") + 1], Is.EqualTo("#2E7D32"));
        }
    }

    [Test, NonParallelizable] // Project.KnownNames is process-wide
    public void The_badge_is_the_initials_and_a_colon_as_the_Orchestrator_titles_tabs()
    {
        // M.LNCH-T69 (M.ORCH-T632): "JR: EZ [Claude]", no longer "[JR] GTFO.EZ [Claude]".
        var look = new LoginLook("Jordan", "JR", "#2E7D32");
        Assert.That(look.Badge("EZ [Claude]"), Is.EqualTo("JR: EZ [Claude]"));
        var wt = new WindowsTerminalLauncher(claudeLook: () => look);
        var saved = Project.KnownNames;
        try
        {
            // Another "GTFO." project must be known for the prefix to be shared (and so stripped) now that the strip
            // is computed from the roster rather than a fixed list.
            Project.KnownNames = ["GTFO.EZ", "GTFO.Dust"];
            Assert.That(wt.BuildAgentTab(new Project { Name = "GTFO.EZ", Path = @"D:\Projects\GTFO.EZ" }, Claude, "mindattic.exe").Title,
                Is.EqualTo("JR: EZ [Claude]"));
        }
        finally { Project.KnownNames = saved; }
    }

    [Test]
    public void A_claude_tab_whose_login_isnt_known_yet_opens_in_the_current_window_as_before()
    {
        var wt = new WindowsTerminalLauncher(claudeLook: () => null);

        var tab = wt.BuildAgentTab(Repo, Claude, "mindattic.exe");

        Assert.That(tab.Window, Is.Null);
        Assert.That(tab.Title, Is.EqualTo("Repo [Claude]"));
        Assert.That(tab.TabColor, Is.EqualTo("#123456"));
        Assert.That(WindowsTerminalLauncher.BuildArgList(tab).Take(3), Is.EqualTo(new[] { "-w", "0", "new-tab" }));
    }

    [Test]
    public void Other_providers_have_no_login_and_keep_the_current_window()
    {
        var wt = new WindowsTerminalLauncher(claudeLook: () => new LoginLook("Jordan", "JO", "#2E7D32"));

        var tab = wt.BuildAgentTab(Repo, Codex, "mindattic.exe");

        Assert.That((tab.Window, tab.Title, tab.TabColor), Is.EqualTo(((string?)null, "Repo [Codex]", "#123456")));
    }

    [Test]
    public void An_unscoped_launchers_login_is_the_global_one_only_while_no_per_cli_login_exists()
    {
        var accounts = Path.Combine(root, ".claude-accounts");
        File.WriteAllText(Path.Combine(root, ".claude.json"), """{ "oauthAccount": { "emailAddress": "ryan@mindattic.com" } }""");

        Assert.That(WindowsTerminalLauncher.DefaultClaudeLook(null, accounts, root)?.Window, Is.EqualTo("ryan"));

        Login("jordan@mindattic.com");
        Assert.That(WindowsTerminalLauncher.DefaultClaudeLook(null, accounts, root), Is.Null, "the tab asks which login once it's open (M.LNCH-T53)");
    }

    [Test]
    public void A_scoped_launchers_login_is_its_scope_and_its_restart_goes_in_that_window()
    {
        var dir = Login("jordan@mindattic.com", """{ "alias": "Jordan" }""");
        var scope = new AccountScope("jordan@mindattic.com", dir, "Jordan");

        Assert.That(WindowsTerminalLauncher.DefaultClaudeLook(scope)?.Window, Is.EqualTo("Jordan"));
        Assert.That(new WindowsTerminalLauncher(scope).ScopeWindow, Is.EqualTo("Jordan"));
        Assert.That(new WindowsTerminalLauncher().ScopeWindow, Is.Null);
    }
}
