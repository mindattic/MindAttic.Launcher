using MindAttic.Launcher.Models;
using NUnit.Framework;

namespace MindAttic.Launcher.Tests;

[TestFixture]
public sealed class ProjectTests
{
    [Test]
    public void TabTitle_strips_the_shared_MindAttic_prefix()
    {
        var project = new Project { Name = "MindAttic.Legion", Path = "" };

        Assert.That(project.TabTitle, Is.EqualTo("Legion"));
    }

    [Test]
    public void TabTitle_prefers_an_explicit_alias_over_the_name()
    {
        var project = new Project { Name = "MindAttic.Legion", TabAlias = "Army", Path = "" };

        Assert.That(project.TabTitle, Is.EqualTo("Army"));
    }

    [Test]
    public void TabTitle_leaves_names_without_the_prefix_untouched()
    {
        var project = new Project { Name = "Standalone", Path = "" };

        Assert.That(project.TabTitle, Is.EqualTo("Standalone"));
    }

    // M.LNCH-T69 (M.ORCH-T632): "JR: EZ [Claude]", so GTFO. and Unity. drop as well; only the exact prefix, dot and all.
    [TestCase("GTFO.EZ", "EZ")]
    [TestCase("Unity.Eve", "Eve")]
    [TestCase("MindAttic.Tickets", "Tickets")]
    [TestCase("UnityEve", "UnityEve")]
    [TestCase("REA.Bloodlines", "REA.Bloodlines")]
    [TestCase("GTFO.", "GTFO.")]
    public void TabTitle_strips_each_shared_prefix(string name, string expected) =>
        Assert.That(new Project { Name = name, Path = "" }.TabTitle, Is.EqualTo(expected));

    [Test]
    public void TabTitle_strips_the_prefix_case_insensitively()
    {
        var project = new Project { Name = "mindattic.Launcher", Path = "" };

        Assert.That(project.TabTitle, Is.EqualTo("Launcher"));
    }
}
