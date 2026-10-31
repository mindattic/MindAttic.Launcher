using System.Text.RegularExpressions;
using NUnit.Framework;

namespace MindAttic.Launcher.Tests;

/// <summary>
/// M.LNCH-T99: the MAT-T68 rename wrote a raw carriage return for the "\r" of "\run.ps1", so the generated run.bat looked for
/// "...\MindAttic.Launcherun.ps1" and Deploy &amp; Run never redeployed. Guards run.ps1 and the run.bat it generates.
/// </summary>
[TestFixture]
public sealed class RunScriptTextTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MindAttic.Launcher.slnx"))) dir = dir.Parent;
        Assert.That(dir, Is.Not.Null, "repo root (MindAttic.Launcher.slnx) not found above the test output");
        return dir!.FullName;
    }

    [TestCase("run.ps1")]
    [TestCase("README.md")]
    public void Has_no_bare_carriage_return(string file)
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot(), file));
        var bare = Regex.Matches(text, "\r(?!\n)");
        Assert.That(bare, Is.Empty, $"{file} has a raw CR not ending a line (a '\\r' turned into a control character)");
    }

    [Test]
    public void Generated_run_bat_points_at_the_repos_run_ps1()
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot(), "run.ps1"));
        Assert.That(text, Does.Contain("set \"RUN_PS1=$repo\\run.ps1\""));
    }
}
