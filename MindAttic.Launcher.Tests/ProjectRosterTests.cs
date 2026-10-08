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
                new Project { Name = "MindAttic.Web",  Path = "" }
            }
        };

        var sorted = ProjectRoster.Sorted(settings).Select(p => p.Name).ToArray();

        Assert.That(sorted, Is.EqualTo(new[] { "Apple", "banana", "MindAttic.Web", "zebra" }));
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

    [Test]
    public void Sorted_with_null_tagged_set_is_unfiltered()
    {
        var settings = new AppSettings
        {
            Projects = { new Project { Name = "MindAttic.Launcher", Path = "", RepoUrl = "https://github.com/mindattic/MindAttic.Launcher" } }
        };

        Assert.That(ProjectRoster.Sorted(settings, taggedFullNames: null), Has.Count.EqualTo(1));
    }

    [Test]
    public void Sorted_keeps_only_projects_whose_repo_is_tagged()
    {
        var settings = new AppSettings
        {
            Projects =
            {
                new Project { Name = "Tagged",   Path = "", RepoUrl = "https://github.com/mindattic/Tagged.git" },
                new Project { Name = "Untagged", Path = "", RepoUrl = "https://github.com/mindattic/Untagged.git" },
            }
        };
        var tagged = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "mindattic/tagged" };

        var sorted = ProjectRoster.Sorted(settings, tagged);

        Assert.That(sorted.Select(p => p.Name), Is.EqualTo(new[] { "Tagged" }));
    }

    [Test]
    public void Sorted_excludes_a_project_with_no_parseable_github_remote_once_a_filter_is_active()
    {
        var settings = new AppSettings
        {
            Projects = { new Project { Name = "NoRemote", Path = "", RepoUrl = null } }
        };
        var tagged = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "someone/else" };

        Assert.That(ProjectRoster.Sorted(settings, tagged), Is.Empty);
    }
}
