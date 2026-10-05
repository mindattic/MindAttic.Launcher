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
    public void Sorted_with_null_starred_set_is_unfiltered()
    {
        var settings = new AppSettings
        {
            Projects = { new Project { Name = "MindAttic.Launcher", Path = "", RepoUrl = "https://github.com/mindattic/MindAttic.Launcher" } }
        };

        Assert.That(ProjectRoster.Sorted(settings, starredFullNames: null), Has.Count.EqualTo(1));
    }

    [Test]
    public void Sorted_keeps_only_projects_whose_repo_is_starred()
    {
        var settings = new AppSettings
        {
            Projects =
            {
                new Project { Name = "Starred",   Path = "", RepoUrl = "https://github.com/mindattic/Starred.git" },
                new Project { Name = "Unstarred", Path = "", RepoUrl = "https://github.com/mindattic/Unstarred.git" },
            }
        };
        var starred = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "mindattic/starred" };

        var sorted = ProjectRoster.Sorted(settings, starred);

        Assert.That(sorted.Select(p => p.Name), Is.EqualTo(new[] { "Starred" }));
    }

    [Test]
    public void Sorted_excludes_a_project_with_no_parseable_github_remote_once_a_filter_is_active()
    {
        var settings = new AppSettings
        {
            Projects = { new Project { Name = "NoRemote", Path = "", RepoUrl = null } }
        };
        var starred = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "someone/else" };

        Assert.That(ProjectRoster.Sorted(settings, starred), Is.Empty);
    }
}
