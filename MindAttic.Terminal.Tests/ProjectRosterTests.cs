using MindAttic.Terminal.Models;
using MindAttic.Terminal.Services;
using NUnit.Framework;

namespace MindAttic.Terminal.Tests;

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
    public void FindByName_is_case_insensitive()
    {
        var settings = new AppSettings
        {
            Projects = { new Project { Name = "MindAttic.Terminal", Path = "" } }
        };

        Assert.That(ProjectRoster.FindByName(settings, "mindattic.terminal"), Is.Not.Null);
        Assert.That(ProjectRoster.FindByName(settings, "unknown"), Is.Null);
    }
}
