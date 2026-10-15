using MindAttic.Launcher.Services;
using NUnit.Framework;

namespace MindAttic.Launcher.Tests;

[TestFixture]
public sealed class WorkspaceCloneServiceTests
{
    private string tempFolder = "";

    [SetUp]
    public void SetUp()
    {
        tempFolder = Path.Combine(Path.GetTempPath(), "MindAtticWorkspaceCloneTests", TestContext.CurrentContext.Test.ID);
        Directory.CreateDirectory(tempFolder);
    }

    [TearDown]
    public void TearDown()
    {
        try { if (Directory.Exists(tempFolder)) Directory.Delete(tempFolder, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    [TestCase("mindattic/MindAttic.Vault", ExpectedResult = "MindAttic.Vault")]
    [TestCase("MindAttic.Vault", ExpectedResult = "MindAttic.Vault")]
    public string DeriveRepoName_takes_the_leaf_after_the_last_slash(string fullName) =>
        WorkspaceCloneService.DeriveRepoName(fullName);

    [Test]
    public void DeriveCloneUrl_builds_an_https_github_url()
    {
        Assert.That(WorkspaceCloneService.DeriveCloneUrl("mindattic/MindAttic.Vault"),
            Is.EqualTo("https://github.com/mindattic/MindAttic.Vault.git"));
    }

    [Test]
    public void CloneAll_skips_a_repo_already_present_without_invoking_git()
    {
        Directory.CreateDirectory(Path.Combine(tempFolder, "MindAttic.Vault"));
        var subject = new WorkspaceCloneService();

        var results = subject.CloneAll(tempFolder, new HashSet<string> { "mindattic/MindAttic.Vault" });

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(results[0].RepoName, Is.EqualTo("MindAttic.Vault"));
            Assert.That(results[0].Ok, Is.True);
            Assert.That(results[0].AlreadyPresent, Is.True);
        });
    }

    [Test]
    public void CloneAll_processes_in_alphabetical_order_and_fires_onDone_for_each()
    {
        Directory.CreateDirectory(Path.Combine(tempFolder, "Beta"));
        Directory.CreateDirectory(Path.Combine(tempFolder, "Alpha"));
        var subject = new WorkspaceCloneService();
        var seen = new List<string>();

        subject.CloneAll(tempFolder,
            new HashSet<string> { "mindattic/Beta", "mindattic/Alpha" },
            onDone: r => seen.Add(r.RepoName));

        Assert.That(seen, Is.EqualTo(new[] { "Alpha", "Beta" }));
    }
}
