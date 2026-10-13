using MindAttic.Launcher.Services;
using NUnit.Framework;

namespace MindAttic.Launcher.Tests;

[TestFixture]
public sealed class ExePathTests
{
    private string temp = "";

    [SetUp]
    public void SetUp()
    {
        temp = Path.Combine(Path.GetTempPath(), "ExePathTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(temp, recursive: true); } catch { /* best-effort */ }
    }

    private string MakeRepo(string name)
    {
        var repo = Path.Combine(temp, name);
        Directory.CreateDirectory(repo);
        File.WriteAllText(Path.Combine(repo, "deploy.ps1"), "");
        File.WriteAllText(Path.Combine(repo, "MindAttic.Launcher.slnx"), "");
        return repo;
    }

    [Test]
    public void ResolveRepoRoot_walks_up_from_a_dev_build_under_bin()
    {
        var repo = MakeRepo("repo");
        var bin = Path.Combine(repo, "MindAttic.Launcher", "bin", "Debug", "net10.0-windows");
        Directory.CreateDirectory(bin);

        Assert.That(ExePath.ResolveRepoRoot(bin), Is.EqualTo(repo));
    }

    [Test]
    public void ResolveRepoRoot_reads_repo_path_next_to_a_deployed_exe()
    {
        var repo = MakeRepo("repo");
        var deploy = Path.Combine(temp, "Apps", "MindAttic.Launcher");
        Directory.CreateDirectory(deploy);
        File.WriteAllText(Path.Combine(deploy, ExePath.RepoPathFile), repo + Environment.NewLine);

        Assert.That(ExePath.ResolveRepoRoot(deploy), Is.EqualTo(repo));
    }

    [Test]
    public void ResolveRepoRoot_ignores_a_repo_path_that_no_longer_points_at_the_repo()
    {
        var deploy = Path.Combine(temp, "Apps", "MindAttic.Launcher");
        Directory.CreateDirectory(deploy);
        File.WriteAllText(Path.Combine(deploy, ExePath.RepoPathFile), Path.Combine(temp, "moved-away"));

        Assert.That(ExePath.ResolveRepoRoot(deploy), Is.Null);
    }

    [Test]
    public void ResolveRepoRoot_needs_the_solution_not_just_any_deploy_ps1()
    {
        // Every C:\Apps-style repo has a deploy.ps1; only the Launcher has this solution.
        var other = Path.Combine(temp, "OtherApp");
        Directory.CreateDirectory(other);
        File.WriteAllText(Path.Combine(other, "deploy.ps1"), "");

        Assert.That(ExePath.ResolveRepoRoot(other), Is.Null);
    }
}
