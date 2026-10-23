using MindAttic.Launcher.Commands;
using MindAttic.Launcher.Services;
using NUnit.Framework;

namespace MindAttic.Launcher.Tests;

[TestFixture]
public sealed class BootstrapCommandTests
{
    private static BootstrapCommand.Phases Recording(List<string> order, string? failing = null)
    {
        Func<bool> Phase(string name) => () => { order.Add(name); return name != failing; };
        return new BootstrapCommand.Phases(
            Prerequisites: Phase("prerequisites"),
            OptionalTools: Phase("optional"),
            AiClis: Phase("ai-clis"),
            RestoreAppData: Phase("appdata"),
            Repos: Phase("repos"),
            StartLocalDb: Phase("localdb"),
            RestoreDatabases: Phase("databases"));
    }

    [Test]
    public void AppData_is_restored_before_the_repos_so_the_GitHub_token_it_holds_can_find_them()
    {
        var order = new List<string>();

        var ok = BootstrapCommand.RunPhases(Recording(order));

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.True);
            Assert.That(order, Is.EqualTo(new[]
            {
                "prerequisites", "optional", "ai-clis", "appdata", "repos", "localdb", "databases"
            }));
        });
    }

    [Test]
    public void LocalDB_is_started_before_the_databases_are_restored()
    {
        var order = new List<string>();

        BootstrapCommand.RunPhases(Recording(order));

        Assert.That(order.IndexOf("localdb"), Is.LessThan(order.IndexOf("databases")));
    }

    [Test]
    public void A_failing_phase_fails_bootstrap_but_never_skips_the_rest()
    {
        var order = new List<string>();

        var ok = BootstrapCommand.RunPhases(Recording(order, failing: "repos"));

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.False);
            Assert.That(order, Has.Count.EqualTo(7));
        });
    }

    private static PrerequisiteService Prereq(Func<string, bool>? exists = null, Func<string, IReadOnlyList<string>, (int, string)>? run = null) =>
        new((exists ?? (_ => true)).Invoke,
            (exe, args, _) => (run ?? ((_, _) => (1, "")))(exe, args),
            refreshPath: () => { },
            fileExists: _ => false);

    private static BootstrapCommand.Seams Fake(
        PrerequisiteService? prereq = null,
        WorkspaceCloneService? cloner = null,
        Func<string, bool>? confirm = null,
        Func<string?>? token = null,
        Func<IReadOnlySet<string>?>? tagged = null,
        Func<string, bool>? restoreAppData = null,
        string backupBase = "") =>
        new(prereq ?? Prereq(),
            cloner ?? new WorkspaceCloneService((_, _, _) => (true, null), _ => (true, null)),
            confirm ?? (_ => false),
            token ?? (() => "tok"),
            tagged ?? (() => new HashSet<string>()),
            restoreAppData ?? (_ => true),
            (_, _) => true,
            _ => true,
            backupBase);

    private static string TempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "bootstrap-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    [Test]
    public void A_missing_GitHub_token_fails_the_repos_phase_without_cloning()
    {
        var cloned = false;
        var cloner = new WorkspaceCloneService((_, _, _) => { cloned = true; return (true, null); }, _ => (true, null));

        var ok = BootstrapCommand.CloneRepos(TempDir(), () => "  ", () => new HashSet<string> { "o/r" }, cloner);

        Assert.Multiple(() => { Assert.That(ok, Is.False); Assert.That(cloned, Is.False); });
    }

    [Test]
    public void A_null_TopicRepoSync_result_fails_the_repos_phase_without_cloning()
    {
        var cloned = false;
        var cloner = new WorkspaceCloneService((_, _, _) => { cloned = true; return (true, null); }, _ => (true, null));

        var ok = BootstrapCommand.CloneRepos(TempDir(), () => "tok", () => null, cloner);

        Assert.Multiple(() => { Assert.That(ok, Is.False); Assert.That(cloned, Is.False); });
    }

    [Test]
    public void A_failed_clone_fails_the_repos_phase()
    {
        var cloner = new WorkspaceCloneService((_, _, _) => (false, "boom"), _ => (true, null));

        var ok = BootstrapCommand.CloneRepos(TempDir(), () => "tok", () => new HashSet<string> { "o/r" }, cloner);

        Assert.That(ok, Is.False);
    }

    [Test]
    public void Skip_optional_never_prompts()
    {
        var prompts = 0;
        var seams = Fake(prereq: Prereq(exists: c => c is "sqlcmd" or "sqllocaldb"), confirm: _ => { prompts++; return false; });
        var settings = new BootstrapCommand.Settings { SkipOptional = true, From = TempDir(), Server = "SomeServer" };

        BootstrapCommand.Run(TempDir(), settings, seams);

        Assert.That(prompts, Is.Zero);
    }

    [Test]
    public void Without_skip_optional_missing_tools_prompt_and_installed_ones_are_skipped()
    {
        var prompts = new List<string>();
        // 'npm' and 'notepad++' on PATH count as installed; everything else is missing (winget list fails).
        var prereq = Prereq(exists: c => c is "npm" or "notepad++");

        BootstrapCommand.OfferOptionalTools(prereq, q => { prompts.Add(q); return false; });

        Assert.Multiple(() =>
        {
            Assert.That(prompts, Has.Count.EqualTo(PrerequisiteService.OptionalTools.Count - 2));
            Assert.That(prompts, Has.None.Contains("Notepad++"));
            Assert.That(prompts, Has.None.Contains("Node.js"));
        });
    }

    [Test]
    public void A_failed_hard_prerequisite_yields_exit_code_1()
    {
        var seams = Fake(prereq: Prereq(exists: c => c != "sqlcmd"));
        var settings = new BootstrapCommand.Settings { SkipOptional = true, From = TempDir(), Server = "SomeServer" };

        Assert.That(BootstrapCommand.Run(TempDir(), settings, seams), Is.EqualTo(1));
    }

    [Test]
    public void A_failed_clone_yields_exit_code_1_and_all_good_yields_0()
    {
        var settings = new BootstrapCommand.Settings { SkipOptional = true, From = TempDir(), Server = "SomeServer" };
        var failing = new WorkspaceCloneService((_, _, _) => (false, "boom"), _ => (true, null));

        Assert.Multiple(() =>
        {
            Assert.That(BootstrapCommand.Run(TempDir(), settings,
                Fake(cloner: failing, tagged: () => new HashSet<string> { "o/r" })), Is.EqualTo(1));
            Assert.That(BootstrapCommand.Run(TempDir(), settings, Fake()), Is.EqualTo(0));
        });
    }

    [Test]
    public void FindNewestBackupFolder_picks_the_lexically_newest_dated_folder()
    {
        var b = TempDir();
        foreach (var n in new[] { "2026-01-05", "2026-10-23", "2026-09-30" }) Directory.CreateDirectory(Path.Combine(b, n));

        Assert.That(BootstrapCommand.FindNewestBackupFolder(b), Is.EqualTo(Path.Combine(b, "2026-10-23")));
    }

    [Test]
    public void FindNewestBackupFolder_is_null_when_the_base_is_missing_or_unreachable()
    {
        var missing = Path.Combine(Path.GetTempPath(), "no-such-" + Guid.NewGuid().ToString("N"));
        var file = Path.Combine(TempDir(), "afile");
        File.WriteAllText(file, "x");

        Assert.Multiple(() =>
        {
            Assert.That(BootstrapCommand.FindNewestBackupFolder(missing), Is.Null);
            Assert.That(BootstrapCommand.FindNewestBackupFolder(file), Is.Null);
            Assert.That(BootstrapCommand.FindNewestBackupFolder(@"\no-such-host-xyz\share"), Is.Null);
        });
    }

    [Test]
    public void An_explicit_from_is_passed_through_to_RestoreCommand_and_beats_the_newest_backup()
    {
        var b = TempDir();
        Directory.CreateDirectory(Path.Combine(b, "2099-01-01"));
        var from = TempDir();
        string? restored = null;
        var seams = Fake(restoreAppData: f => { restored = f; return true; }, backupBase: b);
        var settings = new BootstrapCommand.Settings { SkipOptional = true, From = from, Server = "SomeServer" };

        BootstrapCommand.Run(TempDir(), settings, seams);

        Assert.That(restored, Is.EqualTo(from));
    }
}
