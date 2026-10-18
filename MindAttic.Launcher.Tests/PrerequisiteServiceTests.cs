using MindAttic.Launcher.Services;
using NUnit.Framework;

namespace MindAttic.Launcher.Tests;

[TestFixture]
public sealed class PrerequisiteServiceTests
{
    [Test]
    public void BuildWingetInstallArguments_is_a_silent_unattended_install()
    {
        Assert.That(PrerequisiteService.BuildWingetInstallArguments("Microsoft.sqlcmd"),
            Is.EqualTo(new[]
            {
                "install", "--id", "Microsoft.sqlcmd", "-e", "--silent",
                "--accept-package-agreements", "--accept-source-agreements"
            }));
    }

    [Test]
    public void EnsureHard_skips_install_when_the_command_already_exists()
    {
        var installed = false;
        var subject = new PrerequisiteService(
            exists: _ => true,
            run: (_, _, _) => { installed = true; return (0, ""); });

        var result = subject.EnsureHard("sqlcmd", ["Microsoft.sqlcmd"]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Present, Is.True);
            Assert.That(result.Installed, Is.False);
            Assert.That(installed, Is.False);
        });
    }

    [Test]
    public void EnsureHard_installs_via_the_first_winget_id_that_succeeds()
    {
        var tried = new List<string>();
        var subject = new PrerequisiteService(
            exists: _ => false,
            run: (_, args, _) =>
            {
                tried.Add(args[2]); // --id <value>
                return (0, "");
            });

        var result = subject.EnsureHard("sqllocaldb", ["Microsoft.SQLServer.2022.LocalDB", "Microsoft.SQLServer.2019.LocalDB"]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Present, Is.True);
            Assert.That(result.Installed, Is.True);
            Assert.That(tried, Is.EqualTo(new[] { "Microsoft.SQLServer.2022.LocalDB" }));
        });
    }

    [Test]
    public void EnsureHard_falls_through_to_the_next_winget_id_when_the_first_fails()
    {
        var tried = new List<string>();
        var subject = new PrerequisiteService(
            exists: _ => false,
            run: (_, args, _) =>
            {
                var id = args[2];
                tried.Add(id);
                return id == "Microsoft.SQLServer.2022.LocalDB" ? (1, "not found") : (0, "");
            });

        var result = subject.EnsureHard("sqllocaldb", ["Microsoft.SQLServer.2022.LocalDB", "Microsoft.SQLServer.2019.LocalDB"]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Present, Is.True);
            Assert.That(tried, Is.EqualTo(new[] { "Microsoft.SQLServer.2022.LocalDB", "Microsoft.SQLServer.2019.LocalDB" }));
        });
    }

    [Test]
    public void EnsureHard_reports_failure_when_every_winget_id_fails()
    {
        var subject = new PrerequisiteService(
            exists: _ => false,
            run: (_, _, _) => (1, "package not found"));

        var result = subject.EnsureHard("sqlcmd", ["Microsoft.sqlcmd"]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Present, Is.False);
            Assert.That(result.Error, Does.Contain("sqlcmd"));
        });
    }

    // ── PATH refresh ───────────────────────────────────────────────────────────

    [Test]
    public void A_successful_winget_install_refreshes_this_process_PATH()
    {
        var events = new List<string>();
        var refreshed = false;
        var subject = new PrerequisiteService(
            exists: cmd => { events.Add($"exists {cmd}"); return refreshed; },
            run: (exe, _, _) => { events.Add(exe); return (0, ""); },
            refreshPath: () => { events.Add("refresh"); refreshed = true; });

        var result = subject.EnsureHard("sqlcmd", ["Microsoft.sqlcmd"]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Present, Is.True);
            Assert.That(events, Is.EqualTo(new[] { "exists sqlcmd", "winget", "refresh" }));
        });
    }

    [Test]
    public void A_failed_winget_install_does_not_refresh_PATH()
    {
        var refreshes = 0;
        var subject = new PrerequisiteService(
            exists: _ => false,
            run: (_, _, _) => (1, "no package"),
            refreshPath: () => refreshes++);

        subject.EnsureHard("sqlcmd", ["Microsoft.sqlcmd"]);

        Assert.That(refreshes, Is.Zero);
    }

    [Test]
    public void MergePath_puts_machine_then_user_first_and_keeps_entries_only_this_process_had()
    {
        var merged = PrerequisiteService.MergePath(
            machine: @"C:\Windows;C:\Program Files\Microsoft SQL Server\160\Tools\Binn\",
            user: @"C:\Users\me\AppData\Roaming\npm",
            current: @"C:\Windows;C:\Tools\only-mine;C:\Program Files\Microsoft SQL Server\160\Tools\Binn");

        Assert.That(merged, Is.EqualTo(
            @"C:\Windows;C:\Program Files\Microsoft SQL Server\160\Tools\Binn\;C:\Users\me\AppData\Roaming\npm;C:\Tools\only-mine"));
    }

    [Test]
    public void MergePath_tolerates_missing_parts()
    {
        Assert.That(PrerequisiteService.MergePath(null, "", @"C:\a;;C:\b"), Is.EqualTo(@"C:\a;C:\b"));
    }

    // ── Optional tool detection ───────────────────────────────────────────────

    [Test]
    public void Claude_desktop_and_Antigravity_IDE_are_not_detected_by_their_CLIs()
    {
        var claude = PrerequisiteService.OptionalTools.Single(t => t.Name == "Claude desktop");
        var antigravity = PrerequisiteService.OptionalTools.Single(t => t.Name == "Antigravity IDE");
        // 'claude' and 'agy' on PATH are the CLIs — having them must not hide the desktop apps.
        var subject = new PrerequisiteService(
            exists: cmd => cmd is "claude" or "agy",
            run: (_, _, _) => (1, "No installed package found matching input criteria."));

        Assert.Multiple(() =>
        {
            Assert.That(claude.CommandOnPath, Is.Not.EqualTo("claude"));
            Assert.That(antigravity.CommandOnPath, Is.Not.EqualTo("agy"));
            Assert.That(subject.IsInstalled(claude), Is.False);
            Assert.That(subject.IsInstalled(antigravity), Is.False);
        });
    }

    [Test]
    public void An_optional_tool_is_found_at_its_install_path()
    {
        var antigravity = PrerequisiteService.OptionalTools.Single(t => t.Name == "Antigravity IDE");
        var probed = new List<string>();
        var subject = new PrerequisiteService(
            exists: _ => false,
            run: (_, _, _) => throw new AssertionException("winget should not be asked when the install path exists"),
            fileExists: p => { probed.Add(p); return p.EndsWith(@"Antigravity\Antigravity.exe", StringComparison.OrdinalIgnoreCase); });

        Assert.Multiple(() =>
        {
            Assert.That(subject.IsInstalled(antigravity), Is.True);
            Assert.That(probed, Has.None.Contains("%"), "environment variables are expanded before probing");
        });
    }

    [Test]
    public void An_optional_tool_with_no_command_or_path_falls_back_to_winget_list()
    {
        var vs = PrerequisiteService.OptionalTools.Single(t => t.Name == "Visual Studio Community");
        IReadOnlyList<string>? asked = null;
        var subject = new PrerequisiteService(
            exists: _ => false,
            run: (exe, args, _) => { asked = [exe, .. args]; return (0, "Visual Studio Community 2022 ..."); });

        Assert.Multiple(() =>
        {
            Assert.That(subject.IsInstalled(vs), Is.True);
            Assert.That(asked, Is.EqualTo(new[] { "winget", "list", "--id", vs.WingetId, "-e", "--accept-source-agreements" }));
        });
    }

    // ── AI CLIs ───────────────────────────────────────────────────────────────

    [Test]
    public void AiClis_cover_the_default_providers_CLIs()
    {
        var commands = PrerequisiteService.AiClis.Select(c => c.Command).ToList();
        foreach (var provider in AgentProviderRegistry.Defaults.Where(p => p.Key != "Kimi"))
            Assert.That(commands, Does.Contain(provider.RunCommand.Split(' ')[0]), provider.Key);
    }

    [Test]
    public void EnsureAiCli_skips_a_CLI_already_on_PATH()
    {
        var subject = new PrerequisiteService(
            exists: _ => true,
            run: (_, _, _) => throw new AssertionException("nothing should be installed"));

        var r = subject.EnsureAiCli(new AiCliSpec("Claude Code CLI", "claude", "@anthropic-ai/claude-code", null));

        Assert.Multiple(() =>
        {
            Assert.That(r.Present, Is.True);
            Assert.That(r.Installed, Is.False);
        });
    }

    [Test]
    public void EnsureAiCli_installs_an_npm_CLI_with_npm_cmd_and_refreshes_PATH()
    {
        var installed = false;
        var refreshes = 0;
        var calls = new List<string>();
        var subject = new PrerequisiteService(
            exists: cmd => cmd == "npm" || (cmd == "claude" && installed),
            run: (exe, args, _) => { calls.Add($"{exe} {string.Join(' ', args)}"); installed = true; return (0, ""); },
            refreshPath: () => refreshes++);

        var r = subject.EnsureAiCli(new AiCliSpec("Claude Code CLI", "claude", "@anthropic-ai/claude-code", null));

        Assert.Multiple(() =>
        {
            Assert.That(r.Present, Is.True);
            Assert.That(r.Installed, Is.True);
            Assert.That(calls, Is.EqualTo(new[] { "npm.cmd install -g @anthropic-ai/claude-code" }));
            Assert.That(refreshes, Is.EqualTo(1));
        });
    }

    [Test]
    public void EnsureAiCli_installs_Node_first_when_npm_is_missing()
    {
        var nodeInstalled = false;
        var calls = new List<string>();
        var subject = new PrerequisiteService(
            exists: cmd => cmd == "npm" && nodeInstalled,
            run: (exe, args, _) =>
            {
                calls.Add(exe == "winget" ? $"winget {args[2]}" : exe);
                if (exe == "winget") nodeInstalled = true;
                return (0, "");
            });

        var r = subject.EnsureAiCli(new AiCliSpec("OpenAI Codex CLI", "codex", "@openai/codex", null));

        Assert.Multiple(() =>
        {
            Assert.That(r.Present, Is.True);
            Assert.That(calls, Is.EqualTo(new[] { $"winget {PrerequisiteService.NodeWingetId}", "npm.cmd" }));
        });
    }

    [Test]
    public void EnsureAiCli_installs_a_winget_CLI_via_winget()
    {
        var ids = new List<string>();
        var subject = new PrerequisiteService(
            exists: _ => false,
            run: (exe, args, _) => { ids.Add($"{exe} {args[2]}"); return (0, ""); });

        var r = subject.EnsureAiCli(new AiCliSpec("Antigravity CLI", "agy", null, "Google.AntigravityCLI"));

        Assert.Multiple(() =>
        {
            Assert.That(r.Present, Is.True);
            Assert.That(ids, Is.EqualTo(new[] { "winget Google.AntigravityCLI" }));
        });
    }

    [Test]
    public void EnsureAiCli_reports_a_failed_install_without_throwing()
    {
        var subject = new PrerequisiteService(
            exists: cmd => cmd == "npm",
            run: (_, _, _) => (1, "E404 not found"));

        var r = subject.EnsureAiCli(new AiCliSpec("OpenAI Codex CLI", "codex", "@openai/codex", null));

        Assert.Multiple(() =>
        {
            Assert.That(r.Present, Is.False);
            Assert.That(r.Error, Does.Contain("E404"));
        });
    }

    // ── LocalDB ───────────────────────────────────────────────────────────────

    [TestCase(@"(localdb)\MSSQLLocalDB", ExpectedResult = "MSSQLLocalDB")]
    [TestCase(@"(LocalDB)\Other ", ExpectedResult = "Other")]
    [TestCase("localhost", ExpectedResult = null)]
    [TestCase(@"(localdb)\", ExpectedResult = null)]
    [TestCase(null, ExpectedResult = null)]
    public string? LocalDbInstanceOf_extracts_the_instance_name(string? server) =>
        PrerequisiteService.LocalDbInstanceOf(server);

    [Test]
    public void The_default_LocalDB_server_is_Bobs()
    {
        Assert.That(PrerequisiteService.DefaultLocalDbServer, Is.EqualTo(@"(localdb)\MSSQLLocalDB"));
    }

    [Test]
    public void EnsureLocalDbRunning_starts_an_existing_instance()
    {
        var calls = new List<string>();
        var subject = new PrerequisiteService(
            exists: _ => true,
            run: (exe, args, _) => { calls.Add($"{exe} {string.Join(' ', args)}"); return (0, ""); });

        var (ok, _) = subject.EnsureLocalDbRunning("MSSQLLocalDB");

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.True);
            Assert.That(calls, Is.EqualTo(new[] { "sqllocaldb start MSSQLLocalDB" }));
        });
    }

    [Test]
    public void EnsureLocalDbRunning_creates_and_starts_a_missing_instance()
    {
        var calls = new List<string>();
        var subject = new PrerequisiteService(
            exists: _ => true,
            run: (exe, args, _) =>
            {
                calls.Add($"{exe} {string.Join(' ', args)}");
                return args[0] == "start" ? (1, "The specified LocalDB instance does not exist.") : (0, "");
            });

        var (ok, message) = subject.EnsureLocalDbRunning("MSSQLLocalDB");

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.True);
            Assert.That(message, Does.Contain("created"));
            Assert.That(calls, Is.EqualTo(new[] { "sqllocaldb start MSSQLLocalDB", "sqllocaldb create MSSQLLocalDB -s" }));
        });
    }

    [Test]
    public void EnsureLocalDbRunning_reports_failure_when_create_fails_too()
    {
        var subject = new PrerequisiteService(
            exists: _ => true,
            run: (_, args, _) => (1, $"{args[0]} failed"));

        var (ok, message) = subject.EnsureLocalDbRunning("MSSQLLocalDB");

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.False);
            Assert.That(message, Does.Contain("create failed"));
        });
    }

    [Test]
    public void EnsureLocalDbRunning_reports_a_missing_sqllocaldb_without_throwing()
    {
        var subject = new PrerequisiteService(
            exists: _ => false,
            run: (_, _, _) => throw new System.ComponentModel.Win32Exception("The system cannot find the file specified."));

        var (ok, message) = subject.EnsureLocalDbRunning("MSSQLLocalDB");

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.False);
            Assert.That(message, Does.Contain("sqllocaldb"));
        });
    }
}
