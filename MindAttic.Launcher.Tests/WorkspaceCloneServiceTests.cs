using MindAttic.Launcher.Services;
using NUnit.Framework;

namespace MindAttic.Launcher.Tests;

[TestFixture]
public sealed class WorkspaceCloneServiceTests
{
    private string tempFolder = "";
    private readonly List<string> cloned = [];
    private readonly List<string> pulled = [];

    [SetUp]
    public void SetUp()
    {
        tempFolder = Path.Combine(Path.GetTempPath(), "MindAtticWorkspaceCloneTests", TestContext.CurrentContext.Test.ID);
        Directory.CreateDirectory(tempFolder);
        cloned.Clear();
        pulled.Clear();
    }

    [TearDown]
    public void TearDown()
    {
        try { if (Directory.Exists(tempFolder)) Directory.Delete(tempFolder, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    /// <summary>Fake git: records every call, never runs git or touches the network.</summary>
    private WorkspaceCloneService Subject((bool, string?)? pullResult = null, (bool, string?)? cloneResult = null) => new(
        clone: (_, url, name) => { cloned.Add(name); return cloneResult ?? (true, "cloned"); },
        pull: path => { pulled.Add(Path.GetFileName(path)); return pullResult ?? (true, "up to date"); });

    private void MakeRepo(string name) => Directory.CreateDirectory(Path.Combine(tempFolder, name, ".git"));

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
    public void CloneAll_pulls_a_repo_already_present_instead_of_cloning_it()
    {
        MakeRepo("MindAttic.Vault");

        var results = Subject(pullResult: (true, "updated")).CloneAll(tempFolder, new HashSet<string> { "mindattic/MindAttic.Vault" });

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(results[0].RepoName, Is.EqualTo("MindAttic.Vault"));
            Assert.That(results[0].Ok, Is.True);
            Assert.That(results[0].AlreadyPresent, Is.True);
            Assert.That(results[0].Message, Is.EqualTo("updated"));
            Assert.That(pulled, Is.EqualTo(new[] { "MindAttic.Vault" }));
            Assert.That(cloned, Is.Empty);
        });
    }

    [Test]
    public void CloneAll_reports_a_failed_pull()
    {
        MakeRepo("MindAttic.Vault");

        var results = Subject(pullResult: (false, "fatal: Not possible to fast-forward, aborting."))
            .CloneAll(tempFolder, new HashSet<string> { "mindattic/MindAttic.Vault" });

        Assert.Multiple(() =>
        {
            Assert.That(results[0].Ok, Is.False);
            Assert.That(results[0].Message, Does.Contain("fast-forward"));
        });
    }

    [Test]
    public void CloneAll_treats_a_repo_with_no_upstream_as_fine()
    {
        MakeRepo("Local");

        var results = Subject(pullResult: (false, "There is no tracking information for the current branch."))
            .CloneAll(tempFolder, new HashSet<string> { "mindattic/Local" });

        Assert.That(results[0].Ok, Is.True);
    }

    [Test]
    public void CloneAll_clones_a_missing_repo()
    {
        var results = Subject().CloneAll(tempFolder, new HashSet<string> { "mindattic/New" });

        Assert.Multiple(() =>
        {
            Assert.That(results[0].Ok, Is.True);
            Assert.That(results[0].AlreadyPresent, Is.False);
            Assert.That(cloned, Is.EqualTo(new[] { "New" }));
            Assert.That(pulled, Is.Empty);
        });
    }

    [Test]
    public void CloneAll_clones_into_an_empty_existing_folder()
    {
        Directory.CreateDirectory(Path.Combine(tempFolder, "Empty"));

        var results = Subject().CloneAll(tempFolder, new HashSet<string> { "mindattic/Empty" });

        Assert.Multiple(() =>
        {
            Assert.That(results[0].Ok, Is.True);
            Assert.That(cloned, Is.EqualTo(new[] { "Empty" }));
        });
    }

    [Test]
    public void CloneAll_leaves_a_non_empty_non_git_folder_alone_and_reports_it()
    {
        var dir = Path.Combine(tempFolder, "Stuff");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "notes.txt"), "x");

        var results = Subject().CloneAll(tempFolder, new HashSet<string> { "mindattic/Stuff" });

        Assert.Multiple(() =>
        {
            Assert.That(results[0].Ok, Is.False);
            Assert.That(results[0].Message, Does.Contain("isn't a git repo"));
            Assert.That(cloned, Is.Empty);
            Assert.That(pulled, Is.Empty);
        });
    }

    [Test]
    public void CloneAll_processes_in_alphabetical_order_and_fires_onDone_for_each()
    {
        MakeRepo("Beta");
        MakeRepo("Alpha");
        var seen = new List<string>();

        Subject().CloneAll(tempFolder,
            new HashSet<string> { "mindattic/Beta", "mindattic/Alpha" },
            onDone: r => seen.Add(r.RepoName));

        Assert.That(seen, Is.EqualTo(new[] { "Alpha", "Beta" }));
    }
}
