using MindAttic.Launcher.Models;
using MindAttic.Launcher.Services;
using NUnit.Framework;

namespace MindAttic.Launcher.Tests;

[TestFixture]
public sealed class ProjectRosterTests
{
    [Test]
    public void Sorted_returns_projects_alphabetically_case_insensitive()
    {
        var settings = new AppSettings
        {
            Projects =
            {
                new Project { Name = "zebra",   Path = "" },
                new Project { Name = "Apple",   Path = "" },
                new Project { Name = "banana",  Path = "" },
                new Project { Name = "MindAttic.UiUx", Path = "" }
            }
        };

        var sorted = ProjectRoster.Sorted(settings).Select(p => p.Name).ToArray();

        Assert.That(sorted, Is.EqualTo(new[] { "Apple", "banana", "MindAttic.UiUx", "zebra" }));
    }

    [Test]
    public void Handles_null_projects_without_throwing()
    {
        // "projects": null in settings.json deserializes to a null list — both
        // accessors must treat that as an empty roster, not NRE.
        var settings = new AppSettings { Projects = null! };

        Assert.Multiple(() =>
        {
            Assert.That(ProjectRoster.Sorted(settings), Is.Empty);
            Assert.That(ProjectRoster.FindByName(settings, "anything"), Is.Null);
        });
    }

    [Test]
    public void FindByName_is_case_insensitive()
    {
        var settings = new AppSettings
        {
            Projects = { new Project { Name = "MindAttic.Launcher", Path = "" } }
        };

        Assert.That(ProjectRoster.FindByName(settings, "MindAttic.Launcher"), Is.Not.Null);
        Assert.That(ProjectRoster.FindByName(settings, "unknown"), Is.Null);
    }
}
