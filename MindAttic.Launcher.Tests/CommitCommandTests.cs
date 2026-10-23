using System.Diagnostics;
using MindAttic.Launcher.Commands;
using MindAttic.Launcher.Models;
using MindAttic.Launcher.Services;
using NUnit.Framework;

namespace MindAttic.Launcher.Tests;

[TestFixture]
public sealed class CommitCommandTests
{
    private string root = "";

    [SetUp]
    public void SetUp()
    {
        root = Path.Combine(Path.GetTempPath(), "mco-commitcmd-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
    }

    [TearDown]
    public void TearDown()
    {
        if (!Directory.Exists(root)) return;
        foreach (var f in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(root, recursive: true);
    }

    private WorkspaceCommitter Committer() =>
        new(new GitService(), root, Path.Combine(root, "none", "Tickets.db"));

    private static AppSettings Settings(params Project[] projects) => new() { Projects = [.. projects] };

    private static Project Proj(string name, string path, string? repoUrl = null) =>
        new() { Name = name, Path = path, RepoUrl = repoUrl };

    private static IReadOnlySet<string>? NoFilter() => null;

    [Test]
    public void An_unknown_project_throws_instead_of_silently_doing_nothing()
    {
        var app = Settings(Proj("Alpha", CleanRepo("Alpha")));

        var ex = Assert.Throws<InvalidOperationException>(() =>
            CommitCommand.Run(app, NoFilter, "Nope", null, Committer()));

        Assert.That(ex!.Message, Does.Contain("Unknown project: Nope"));
    }

    [Test]
    public void A_missing_path_fails_the_run_but_the_loop_continues_to_the_rest()
    {
        var app = Settings(Proj("Alpha", Path.Combine(root, "gone")), Proj("Beta", CleanRepo("Beta")));

        var (exit, results) = CommitCommand.Run(app, NoFilter, null, null, Committer());

        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.EqualTo(1));
            Assert.That(results.Select(r => r.Project), Is.EqualTo(new[] { "Alpha", "Beta" }));
            Assert.That(results[0].Ok, Is.False);
            Assert.That(results[0].Detail, Is.EqualTo("path not found"));
            Assert.That(results[1].Ok, Is.True, "the repo after the failure was still processed");
        });
    }

    [Test]
    public void A_path_that_is_not_a_git_repo_has_an_invalid_status_and_fails_but_the_loop_continues()
    {
        var notRepo = Path.Combine(root, "plain");
        Directory.CreateDirectory(notRepo);
        var app = Settings(Proj("Alpha", notRepo), Proj("Beta", CleanRepo("Beta")));

        var (exit, results) = CommitCommand.Run(app, NoFilter, null, null, Committer());

        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.EqualTo(1));
            Assert.That(results, Has.Count.EqualTo(2));
            Assert.That(results[0].Ok, Is.False);
            Assert.That(results[1].Ok, Is.True);
        });
    }

    [Test]
    public void Clean_repos_report_nothing_to_commit_and_do_not_fail_the_run()
    {
        var app = Settings(Proj("Alpha", CleanRepo("Alpha")), Proj("Beta", CleanRepo("Beta")));

        var (exit, results) = CommitCommand.Run(app, NoFilter, null, null, Committer());

        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.EqualTo(0));
            Assert.That(results.Select(r => r.Detail), Is.All.EqualTo("nothing to commit"));
        });
    }

    [Test]
    public void A_failed_commit_returns_1()
    {
        var path = CleanRepo("Alpha");
        File.WriteAllText(Path.Combine(path, "a.cs"), "class A {}");

        // No ticket database exists, so the commit can't be aligned to a ticket and fails.
        var (exit, results) = CommitCommand.Run(Settings(Proj("Alpha", path)), NoFilter, null, "Add A", Committer());

        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.EqualTo(1));
            Assert.That(results.Single().Ok, Is.False);
        });
    }

    [Test]
    public void A_failed_push_of_an_earlier_commit_returns_1()
    {
        var bare = Path.Combine(root, "remote.git");
        Git(root, "init", "-q", "--bare", "-b", "main", bare);
        var path = CleanRepo("Alpha");
        Git(path, "remote", "add", "origin", bare);
        Git(path, "push", "-q", "-u", "origin", "main");
        File.WriteAllText(Path.Combine(path, "b.txt"), "y");
        Git(path, "add", "-A");
        Git(path, "commit", "-q", "-m", "unpushed");
        Git(path, "remote", "set-url", "origin", Path.Combine(root, "gone.git"));

        var (exit, results) = CommitCommand.Run(Settings(Proj("Alpha", path)), NoFilter, null, null, Committer());

        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.EqualTo(1));
            Assert.That(results.Single().Ok, Is.False);
        });
    }

    [Test]
    public void The_default_targets_are_the_sorted_roster_filtered_by_the_topic_set()
    {
        var app = Settings(
            Proj("Zeta", CleanRepo("Zeta"), "https://github.com/o/zeta"),
            Proj("Alpha", CleanRepo("Alpha"), "https://github.com/o/alpha"),
            Proj("Untagged", CleanRepo("Untagged"), "https://github.com/o/untagged"));
        IReadOnlySet<string>? tagged = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "o/zeta", "o/alpha" };

        var (_, results) = CommitCommand.Run(app, () => tagged, null, null, Committer());

        Assert.That(results.Select(r => r.Project), Is.EqualTo(new[] { "Alpha", "Zeta" }));
    }

    [Test]
    public void A_named_project_ignores_the_topic_filter()
    {
        var app = Settings(Proj("Untagged", CleanRepo("Untagged"), "https://github.com/o/untagged"));
        IReadOnlySet<string>? tagged = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "o/other" };
        var topicConsulted = false;

        var (exit, results) = CommitCommand.Run(app, () => { topicConsulted = true; return tagged; }, "untagged", null, Committer());

        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.EqualTo(0));
            Assert.That(results.Select(r => r.Project), Is.EqualTo(new[] { "Untagged" }));
            Assert.That(topicConsulted, Is.False);
        });
    }

    private string CleanRepo(string name)
    {
        var path = Path.Combine(root, name);
        Directory.CreateDirectory(path);
        Git(path, "init", "-q", "-b", "main");
        Git(path, "config", "user.name", "Test");
        Git(path, "config", "user.email", "test@example.com");
        Git(path, "config", "commit.gpgsign", "false");
        File.WriteAllText(Path.Combine(path, "seed.txt"), "x");
        Git(path, "add", "-A");
        Git(path, "commit", "-q", "-m", "seed");
        return path;
    }

    private static string Git(string repo, params string[] args)
    {
        var psi = new ProcessStartInfo("git") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        psi.ArgumentList.Add("-C");
        psi.ArgumentList.Add(repo);
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd();
        p.StandardError.ReadToEnd();
        p.WaitForExit();
        return output.Trim();
    }
}
