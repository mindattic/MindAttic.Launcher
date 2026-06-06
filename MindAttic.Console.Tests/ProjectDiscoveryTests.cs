using MindAttic.Console.Models;
using MindAttic.Console.Services;
using NUnit.Framework;

namespace MindAttic.Console.Tests;

[TestFixture]
public sealed class ProjectDiscoveryTests
{
    private string root = "";

    [SetUp]
    public void SetUp()
    {
        root = Path.Combine(Path.GetTempPath(), "MindAtticDiscoveryTests", TestContext.CurrentContext.Test.ID);
        Directory.CreateDirectory(root);
    }

    [TearDown]
    public void TearDown()
    {
        try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    private string MakeRepo(string name, bool gitAsFile = false)
    {
        var dir = Path.Combine(root, name);
        Directory.CreateDirectory(dir);
        var dotGit = Path.Combine(dir, ".git");
        if (gitAsFile) File.WriteAllText(dotGit, "gitdir: ../somewhere"); // worktree form
        else Directory.CreateDirectory(dotGit);
        return dir;
    }

    private string MakePlainDir(string name)
    {
        var dir = Path.Combine(root, name);
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Test]
    public void FindUnregistered_returns_only_unknown_git_repos_sorted()
    {
        MakeRepo("Zeta");                       // candidate (.git dir)
        MakeRepo("Alpha", gitAsFile: true);     // candidate (.git file / worktree)
        MakePlainDir("NotARepo");               // excluded — no .git
        var registered = MakeRepo("Registered");
        var ignored = MakeRepo("Ignored");

        var settings = new AppSettings
        {
            Projects = { new Project { Name = "Registered", Path = registered } },
            DiscoveryIgnore = [ignored],
        };

        var found = ProjectDiscovery.FindUnregistered(settings, root);

        Assert.That(found.Select(r => r.Name), Is.EqualTo(new[] { "Alpha", "Zeta" }));
        Assert.That(found.Select(r => r.Path), Is.EqualTo(new[]
        {
            Path.Combine(root, "Alpha"),
            Path.Combine(root, "Zeta"),
        }));
    }

    [Test]
    public void FindUnregistered_matches_registered_paths_with_trailing_separator()
    {
        var repo = MakeRepo("Trailing");
        var settings = new AppSettings
        {
            Projects = { new Project { Name = "Trailing", Path = repo + Path.DirectorySeparatorChar } },
        };

        Assert.That(ProjectDiscovery.FindUnregistered(settings, root), Is.Empty);
    }

    [Test]
    public void FindUnregistered_returns_empty_when_root_missing()
    {
        var settings = new AppSettings();
        Assert.That(ProjectDiscovery.FindUnregistered(settings, Path.Combine(root, "does-not-exist")), Is.Empty);
    }

    [Test]
    public void FindUnregistered_tolerates_null_collections()
    {
        MakeRepo("Solo");
        var settings = new AppSettings { Projects = null!, DiscoveryIgnore = null };

        var found = ProjectDiscovery.FindUnregistered(settings, root);

        Assert.That(found.Select(r => r.Name), Is.EqualTo(new[] { "Solo" }));
    }
}
