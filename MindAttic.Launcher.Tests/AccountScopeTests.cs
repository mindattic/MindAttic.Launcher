using System.IO;
using System.Linq;
using MindAttic.Launcher.Models;
using MindAttic.Launcher.Services;
using NUnit.Framework;

namespace MindAttic.Launcher.Tests;

/// <summary>A Launcher scoped to one Claude login (M.LNCH-T54).</summary>
[TestFixture]
public sealed class AccountScopeTests
{
    private string root = "";

    [SetUp]
    public void SetUp()
    {
        root = Path.Combine(Path.GetTempPath(), "launcher-scope-" + Path.GetRandomFileName());
        Directory.CreateDirectory(root);
    }

    [TearDown]
    public void TearDown()
    {
        AccountScope.SignInOverride = null;
        try { Directory.Delete(root, recursive: true); } catch (IOException) { }
    }

    private string Folder(string email) => Path.Combine(root, email);

    private void SignIn(string email, string signedInAs)
    {
        Directory.CreateDirectory(Folder(email));
        File.WriteAllText(Path.Combine(Folder(email), ".claude.json"),
            $$$"""{"oauthAccount":{"emailAddress":"{{{signedInAs}}}"}}""");
    }

    private void Look(string email, string json) =>
        File.WriteAllText(Path.Combine(Folder(email), AccountScope.LookFileName), json);

    [Test]
    public void For_names_the_per_cli_login_folder_of_the_normalized_email()
    {
        var scope = AccountScope.For("  Jordan@MindAttic.com ", root)!;

        Assert.That(scope.Email, Is.EqualTo("jordan@mindattic.com"));
        Assert.That(scope.ConfigDir, Is.EqualTo(Folder("jordan@mindattic.com")));
    }

    [Test]
    public void For_rejects_something_that_isnt_an_email() =>
        Assert.That(AccountScope.For("jordan", root), Is.Null);

    [Test]
    public void Alias_comes_from_the_login_folders_file_while_it_is_for_the_signed_in_email()
    {
        SignIn("jordan@mindattic.com", "jordan@mindattic.com");
        Look("jordan@mindattic.com", """{"email":"jordan@mindattic.com","alias":"Jordan"}""");

        Assert.That(AccountScope.For("jordan@mindattic.com", root)!.Alias, Is.EqualTo("Jordan"));
    }

    [Test]
    public void Alias_without_an_email_in_the_file_is_trusted_in_the_per_cli_folder_named_for_it()
    {
        SignIn("jordan@mindattic.com", "jordan@mindattic.com");
        Look("jordan@mindattic.com", """{"alias":"Jordan"}""");

        Assert.That(AccountScope.For("jordan@mindattic.com", root)!.Alias, Is.EqualTo("Jordan"));
    }

    [Test]
    public void Alias_written_for_another_email_is_ignored_and_falls_back_to_the_local_part()
    {
        SignIn("jordan@mindattic.com", "jordan@mindattic.com");
        Look("jordan@mindattic.com", """{"email":"amy@mindattic.com","alias":"Amy"}""");

        Assert.That(AccountScope.For("jordan@mindattic.com", root)!.Alias, Is.EqualTo("jordan"));
    }

    [Test]
    public void Alias_without_a_file_is_the_emails_part_before_the_at() =>
        Assert.That(AccountScope.For("jordan@mindattic.com", root)!.Alias, Is.EqualTo("jordan"));

    [Test]
    public void Signed_in_only_when_the_folder_holds_that_emails_login()
    {
        Assert.That(AccountScope.For("jordan@mindattic.com", root)!.IsSignedIn, Is.False);

        SignIn("jordan@mindattic.com", "amy@mindattic.com");
        Assert.That(AccountScope.For("jordan@mindattic.com", root)!.IsSignedIn, Is.False);

        SignIn("jordan@mindattic.com", "Jordan@mindattic.com");
        Assert.That(AccountScope.For("jordan@mindattic.com", root)!.IsSignedIn, Is.True);
    }

    [Test]
    public void Ensure_leaves_a_signed_in_login_alone()
    {
        SignIn("jordan@mindattic.com", "jordan@mindattic.com");
        var ran = false;
        AccountScope.SignInOverride = _ => { ran = true; return 0; };

        var (scope, error) = AccountScope.Ensure("jordan@mindattic.com", root);

        Assert.That(error, Is.Null);
        Assert.That(scope!.Email, Is.EqualTo("jordan@mindattic.com"));
        Assert.That(ran, Is.False);
    }

    [Test]
    public void Ensure_signs_a_login_that_isnt_in_yet_in_first()
    {
        string? signedIn = null;
        AccountScope.SignInOverride = email =>
        {
            signedIn = email;
            SignIn(email, email);
            Look(email, $$"""{"email":"{{email}}","alias":"Jordan"}""");
            return 0;
        };

        var (scope, error) = AccountScope.Ensure("Jordan@mindattic.com", root);

        Assert.That(error, Is.Null);
        Assert.That(signedIn, Is.EqualTo("jordan@mindattic.com"));
        Assert.That(scope!.Alias, Is.EqualTo("Jordan"));
    }

    [Test]
    public void Ensure_refuses_to_open_on_a_login_the_sign_in_didnt_finish()
    {
        AccountScope.SignInOverride = _ => 1;

        var (scope, error) = AccountScope.Ensure("jordan@mindattic.com", root);

        Assert.That(scope, Is.Null);
        Assert.That(error, Does.Contain("jordan@mindattic.com"));
    }

    [Test]
    public void A_scoped_launcher_puts_the_login_folder_in_every_claude_tabs_command_and_the_alias_in_its_title()
    {
        var scope = new AccountScope("jordan@mindattic.com", @"C:\Users\x\.claude-accounts\jordan@mindattic.com", "Jordan");
        var wt = new WindowsTerminalLauncher(scope);
        var claude = new AgentProvider { Key = "Claude", Name = "Claude Code", RunCommand = "claude" };
        var project = new Project { Name = "Repo", Path = @"D:\Projects\Repo" };

        foreach (var tab in new[]
                 {
                     wt.BuildAgentTab(project, claude, "mindattic.exe", "go"),
                     wt.BuildAgentTabAtPath("General [Claude]", @"D:\Projects", claude, "mindattic.exe", prompt: "go"),
                 })
        {
            var args = tab.Command.ToList();
            var at = args.IndexOf("--config-dir");
            Assert.That(at, Is.GreaterThan(0));
            Assert.That(args[at + 1], Is.EqualTo(scope.ConfigDir));
            Assert.That(tab.Title, Does.EndWith("— Jordan"));
            Assert.That(args[args.IndexOf("--title") + 1], Is.EqualTo(tab.Title));
            Assert.That(args[^2], Is.EqualTo("--prompt"), "the prompt stays the host's last argument");
        }
    }

    [Test]
    public void A_scoped_launcher_leaves_other_providers_tabs_alone()
    {
        var wt = new WindowsTerminalLauncher(new AccountScope("jordan@mindattic.com", @"C:\x", "Jordan"));
        var codex = new AgentProvider { Key = "Codex", Name = "OpenAI Codex", RunCommand = "codex" };

        var tab = wt.BuildAgentTab(new Project { Name = "Repo", Path = @"D:\Projects\Repo" }, codex, "mindattic.exe");

        Assert.That(tab.Command, Does.Not.Contain("--config-dir"));
        Assert.That(tab.Title, Does.Not.Contain("Jordan"));
    }

    [Test]
    public void An_unscoped_launcher_sets_no_login_folder()
    {
        var claude = new AgentProvider { Key = "Claude", Name = "Claude Code", RunCommand = "claude" };
        var tab = new WindowsTerminalLauncher().BuildAgentTab(new Project { Name = "Repo", Path = @"D:\Projects\Repo" }, claude, "mindattic.exe");

        Assert.That(tab.Command, Does.Not.Contain("--config-dir"));
    }
}
