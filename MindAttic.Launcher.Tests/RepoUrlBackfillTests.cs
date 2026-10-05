using MindAttic.Launcher.Models;
using MindAttic.Launcher.Services;
using MindAttic.Vault.Settings;
using NUnit.Framework;

namespace MindAttic.Launcher.Tests;

[TestFixture]
public sealed class RepoUrlBackfillTests
{
    private string tempRoot = "";
    private SettingsStore subject = null!;

    [SetUp]
    public void SetUp()
    {
        tempRoot = Path.Combine(Path.GetTempPath(), "MindAttic.Launcher.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        var store = new JsonSettingsStore<AppSettings>(Path.Combine(tempRoot, "settings"));
        subject = new SettingsStore(store);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(tempRoot, recursive: true); }
        catch { }
    }

    [Test]
    public void Fills_in_a_blank_RepoUrl_from_the_resolved_remote_and_persists_it()
    {
        subject.Update(s => s.Projects.Add(
            new Project { Name = "Prose", Path = @"D:\Projects\MindAttic\Prose", RepoUrl = null }));

        RepoUrlBackfill.Run(subject, _ => "https://github.com/mindattic/Prose");

        var reloaded = subject.Load();
        Assert.That(reloaded.Projects.Single(p => p.Name == "Prose").RepoUrl,
            Is.EqualTo("https://github.com/mindattic/Prose"));
    }

    [Test]
    public void Leaves_an_already_populated_RepoUrl_untouched()
    {
        subject.Update(s => s.Projects.Add(
            new Project { Name = "Launcher", Path = "irrelevant", RepoUrl = "https://github.com/mindattic/MindAttic.Launcher" }));

        RepoUrlBackfill.Run(subject, _ => "https://github.com/someone/else");

        var reloaded = subject.Load();
        Assert.That(reloaded.Projects.Single().RepoUrl, Is.EqualTo("https://github.com/mindattic/MindAttic.Launcher"));
    }

    [Test]
    public void Leaves_RepoUrl_null_when_no_remote_can_be_resolved()
    {
        subject.Update(s => s.Projects.Add(
            new Project { Name = "NoRemote", Path = "irrelevant", RepoUrl = null }));

        RepoUrlBackfill.Run(subject, _ => null);

        var reloaded = subject.Load();
        Assert.That(reloaded.Projects.Single().RepoUrl, Is.Null);
    }

    [Test]
    public void Does_not_touch_the_resolver_when_every_RepoUrl_is_already_populated()
    {
        subject.Update(s => s.Projects.Add(
            new Project { Name = "Launcher", Path = "irrelevant", RepoUrl = "https://github.com/mindattic/MindAttic.Launcher" }));

        var resolverCalled = false;
        RepoUrlBackfill.Run(subject, _ => { resolverCalled = true; return "https://github.com/someone/else"; });

        Assert.That(resolverCalled, Is.False);
    }
}
