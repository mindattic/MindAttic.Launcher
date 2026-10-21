using MindAttic.Launcher.Models;
using MindAttic.Launcher.Services;
using NUnit.Framework;

namespace MindAttic.Launcher.Tests;

[TestFixture]
public sealed class ProjectTreeTests
{
    private static Project P(string name) => new() { Name = name, Path = "" };

    [Test]
    public void Ungrouped_project_is_a_leaf_at_the_root()
    {
        var projects = new[] { P("JobHunt"), P("ThinkTank") };

        var level = ProjectTree.Level(projects, path: []);

        Assert.That(level, Is.EquivalentTo(new ProjectTree.Entry[]
        {
            new ProjectTree.ProjectEntry("JobHunt", projects[0]),
            new ProjectTree.ProjectEntry("ThinkTank", projects[1]),
        }));
    }

    [Test]
    public void Shared_first_segment_collapses_into_one_group_at_the_root()
    {
        var projects = new[]
        {
            P("MindAttic.Authentication"),
            P("MindAttic.Vault"),
            P("JobHunt"),
        };

        var level = ProjectTree.Level(projects, path: []);

        Assert.That(level, Is.EquivalentTo(new ProjectTree.Entry[]
        {
            new ProjectTree.GroupEntry("MindAttic", 2),
            new ProjectTree.ProjectEntry("JobHunt", projects[2]),
        }));
    }

    [Test]
    public void Descending_into_a_group_strips_the_consumed_prefix()
    {
        var projects = new[] { P("MindAttic.Authentication"), P("MindAttic.Vault") };

        var level = ProjectTree.Level(projects, path: ["MindAttic"]);

        Assert.That(level, Is.EquivalentTo(new ProjectTree.Entry[]
        {
            new ProjectTree.ProjectEntry("Authentication", projects[0]),
            new ProjectTree.ProjectEntry("Vault", projects[1]),
        }));
    }

    [Test]
    public void A_project_that_is_also_a_group_prefix_surfaces_as_a_leaf_beside_its_children()
    {
        // GTFO.EZ is itself a project, but GTFO.EZ.AbsurdItems/.Shared extend past it.
        var ez = P("GTFO.EZ");
        var absurdItems = P("GTFO.EZ.AbsurdItems");
        var shared = P("GTFO.EZ.Shared");
        var projects = new[] { ez, absurdItems, shared };

        var root = ProjectTree.Level(projects, path: []);
        Assert.That(root, Is.EquivalentTo(new ProjectTree.Entry[] { new ProjectTree.GroupEntry("GTFO", 3) }));

        var gtfo = ProjectTree.Level(projects, path: ["GTFO"]);
        Assert.That(gtfo, Is.EquivalentTo(new ProjectTree.Entry[] { new ProjectTree.GroupEntry("EZ", 3) }));

        var ezLevel = ProjectTree.Level(projects, path: ["GTFO", "EZ"]);
        Assert.That(ezLevel, Is.EquivalentTo(new ProjectTree.Entry[]
        {
            new ProjectTree.ProjectEntry("EZ", ez),
            new ProjectTree.ProjectEntry("AbsurdItems", absurdItems),
            new ProjectTree.ProjectEntry("Shared", shared),
        }));
    }

    [Test]
    public void A_lone_project_three_segments_deep_stays_a_single_flat_leaf()
    {
        var project = P("Foo.Bar.Baz");

        var level = ProjectTree.Level([project], path: []);

        Assert.That(level, Is.EquivalentTo(new ProjectTree.Entry[]
        {
            new ProjectTree.ProjectEntry("Foo.Bar.Baz", project),
        }));
    }

    [Test]
    public void Duplicate_names_collapse_into_a_group_then_each_surface_as_their_own_leaf_inside_it()
    {
        var first = P("MindAttic.Unity");
        var second = P("MindAttic.Unity");

        var underMindAttic = ProjectTree.Level([first, second], path: ["MindAttic"]);
        Assert.That(underMindAttic, Is.EquivalentTo(new ProjectTree.Entry[] { new ProjectTree.GroupEntry("Unity", 2) }));

        var underUnity = ProjectTree.Level([first, second], path: ["MindAttic", "Unity"]);
        Assert.That(underUnity, Has.Count.EqualTo(2));
        Assert.That(underUnity, Has.All.Matches<ProjectTree.Entry>(e => e is ProjectTree.ProjectEntry { Name: "Unity" }));
    }

    [Test]
    public void Empty_roster_yields_an_empty_level()
    {
        Assert.That(ProjectTree.Level([], path: []), Is.Empty);
    }
}
