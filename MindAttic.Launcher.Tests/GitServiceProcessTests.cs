using System.Diagnostics;
using MindAttic.Launcher.Models;
using MindAttic.Launcher.Services;
using NUnit.Framework;

namespace MindAttic.Launcher.Tests;

/// <summary>GitService's process methods against throwaway repos with a local bare remote.</summary>
[TestFixture]
public sealed class GitServiceProcessTests
{
    private string _root = "";
    private string _bare = "";
    private string _work = "";
    private readonly GitService _git = new();

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "gitsvc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _bare = Path.Combine(_root, "remote.git");
        _work = Path.Combine(_root, "work");
        Directory.CreateDirectory(_work);

        G(_root, "init", "--bare", "-b", "main", _bare);
        G(_work, "init", "-b", "main");
        Identify(_work);
        File.WriteAllText(Path.Combine(_work, "seed.txt"), "seed");
        G(_work, "add", "-A");
        G(_work, "commit", "-m", "seed");
        G(_work, "remote", "add", "origin", _bare);
        G(_work, "push", "-u", "origin", "main");
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
                File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(_root, true);
        }
        catch { }
    }

    private static void Identify(string repo)
    {
        G(repo, "config", "user.name", "Test");
        G(repo, "config", "user.email", "test@example.com");
    }

    private static string G(string dir, params string[] args)
    {
        var psi = new ProcessStartInfo("git") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = dir };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var o = p.StandardOutput.ReadToEndAsync();
        var e = p.StandardError.ReadToEndAsync();
        p.WaitForExit();
        Assert.That(p.ExitCode, Is.EqualTo(0), $"git {string.Join(' ', args)}: {e.Result}");
        return o.Result.Trim();
    }

    private string CloneOther()
    {
        var other = Path.Combine(_root, "other");
        G(_root, "clone", _bare, other);
        Identify(other);
        return other;
    }

    [Test]
    public void Commit_reports_the_status_error_for_a_non_repo()
    {
        var dir = Path.Combine(_root, "plain");
        Directory.CreateDirectory(dir);
        var (ok, message, commit) = _git.Commit(dir);
        Assert.That(ok, Is.False);
        Assert.That(message, Is.EqualTo("not a git repo"));
        Assert.That(commit, Is.EqualTo(""));
    }

    [Test]
    public void Commit_has_nothing_to_commit_for_a_clean_tree()
    {
        var (ok, message, _) = _git.Commit(_work);
        Assert.That(ok, Is.True);
        Assert.That(message, Is.EqualTo("nothing to commit"));
    }

    [Test]
    public void Commit_uses_the_trimmed_user_message_and_syncs()
    {
        File.WriteAllText(Path.Combine(_work, "a.txt"), "a");
        var (ok, message, commit) = _git.Commit(_work, "  my message  ");
        Assert.That(ok, Is.True);
        Assert.That(message, Is.EqualTo("synced"));
        Assert.That(commit, Is.EqualTo("my message"));
        Assert.That(G(_work, "log", "-1", "--format=%s"), Is.EqualTo("my message"));
        Assert.That(G(_bare, "log", "-1", "--format=%s", "main"), Is.EqualTo("my message"));
    }

    [Test]
    public void Commit_uses_AutoMessage_when_no_user_message()
    {
        File.WriteAllText(Path.Combine(_work, "b.txt"), "b");
        var (ok, _, commit) = _git.Commit(_work, "   ");
        Assert.That(ok, Is.True);
        Assert.That(commit, Is.EqualTo("Add b.txt"));
        Assert.That(G(_work, "log", "-1", "--format=%s"), Is.EqualTo("Add b.txt"));
    }

    [Test]
    public void Commit_reports_git_push_failed_when_the_remote_rejects()
    {
        var hook = Path.Combine(_bare, "hooks", "pre-receive");
        File.WriteAllText(hook, "#!/bin/sh\necho no pushes here >&2\nexit 1\n");
        File.WriteAllText(Path.Combine(_work, "c.txt"), "c");
        var (ok, message, commit) = _git.Commit(_work, "will be rejected");
        Assert.That(ok, Is.False);
        Assert.That(message, Does.StartWith(GitService.PushFailedPrefix));
        Assert.That(commit, Is.EqualTo("will be rejected"));
    }

    [Test]
    public void Pull_fast_forwards_to_the_remote()
    {
        var other = CloneOther();
        File.WriteAllText(Path.Combine(other, "n.txt"), "n");
        G(other, "add", "-A");
        G(other, "commit", "-m", "more");
        G(other, "push");

        var first = _git.Pull(_work);
        Assert.That(first.ok, Is.True);
        Assert.That(first.message, Is.EqualTo("updated"));
        Assert.That(File.Exists(Path.Combine(_work, "n.txt")), Is.True);

        var second = _git.Pull(_work);
        Assert.That(second, Is.EqualTo((true, (string?)"up to date")));
    }

    [Test]
    public void Pull_without_a_remote_fails_in_a_way_IsNoUpstream_recognizes()
    {
        var local = Path.Combine(_root, "local");
        Directory.CreateDirectory(local);
        G(local, "init", "-b", "main");
        var (ok, message) = _git.Pull(local);
        Assert.That(ok, Is.False);
        Assert.That(GitService.IsNoUpstream(message), Is.True, message);
    }

    [Test]
    public void RemoteUrl_returns_origin_or_null()
    {
        Assert.That(_git.RemoteUrl(_work), Is.EqualTo(_bare).Or.EqualTo(_bare.Replace("\\", "/")));

        var local = Path.Combine(_root, "local");
        Directory.CreateDirectory(local);
        G(local, "init", "-b", "main");
        Assert.That(_git.RemoteUrl(local), Is.Null);
        Assert.That(_git.RemoteUrl(Path.Combine(_root, "missing")), Is.Null);
    }

    [Test]
    public void Clone_reports_cloned_or_the_git_error()
    {
        var (ok, message) = _git.Clone(_root, _bare, "cloned");
        Assert.That(ok, Is.True);
        Assert.That(message, Is.EqualTo("cloned"));
        Assert.That(File.Exists(Path.Combine(_root, "cloned", "seed.txt")), Is.True);

        var bad = _git.Clone(_root, Path.Combine(_root, "nope.git"), "bad");
        Assert.That(bad.ok, Is.False);
        Assert.That(bad.message, Does.Contain("fatal").IgnoreCase);
    }

    [Test]
    public void FetchShortStatuses_maps_each_project_to_its_short_status()
    {
        var dirty = Path.Combine(_root, "dirty");
        G(_root, "clone", _bare, dirty);
        File.WriteAllText(Path.Combine(dirty, "x.txt"), "x");

        var result = _git.FetchShortStatuses(
        [
            new Project { Name = "clean", Path = _work },
            new Project { Name = "dirty", Path = dirty },
            new Project { Name = "gone", Path = Path.Combine(_root, "missing") },
        ]);

        Assert.That(result["clean"], Is.EqualTo("clean"));
        Assert.That(result["dirty"], Is.EqualTo("1 change(s) [+1]"));
        Assert.That(result["gone"], Is.EqualTo("PATH NOT FOUND"));
    }
}
