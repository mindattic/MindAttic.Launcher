using MindAttic.Launcher.Models;
using MindAttic.Launcher.Services;
using NUnit.Framework;

namespace MindAttic.Launcher.Tests;

[TestFixture]
public sealed class ProjectCompanionServiceTests
{
    [TearDown]
    public void TearDown()
    {
        ProjectCompanionService.ResetForTesting();
    }

    [Test]
    public void EnsureStarted_is_noop_for_non_prose_project()
    {
        var checkedUrl = false;
        var launched = false;
        ProjectCompanionService.HealthChecker = _ => { checkedUrl = true; return false; };
        ProjectCompanionService.ProcessLauncher = _ => { launched = true; };

        var project = new Project { Name = "Tutor", Path = @"D:\Projects\MindAttic\Tutor" };
        ProjectCompanionService.EnsureStarted(project, project.Path);

        Assert.That(checkedUrl, Is.False);
        Assert.That(launched, Is.False);
    }

    [Test]
    public void EnsureStarted_is_noop_when_prose_hub_already_healthy()
    {
        var launched = false;
        ProjectCompanionService.HealthChecker = url =>
        {
            Assert.That(url, Is.EqualTo(ProjectCompanionService.ProseHealthUrl));
            return true;
        };
        ProjectCompanionService.ProcessLauncher = _ => { launched = true; };

        var project = new Project { Name = "Prose", Path = @"D:\Projects\MindAttic\Prose" };
        ProjectCompanionService.EnsureStarted(project, project.Path);

        Assert.That(launched, Is.False);
    }

    [Test]
    public void EnsureStarted_invokes_launcher_when_prose_hub_unhealthy()
    {
        var launchDir = "";
        var attempts = 0;
        ProjectCompanionService.HealthChecker = _ =>
        {
            attempts++;
            return attempts > 1; // Unhealthy on first check, healthy after launch
        };
        ProjectCompanionService.ProcessLauncher = dir => { launchDir = dir; };

        var project = new Project { Name = "Prose", Path = @"D:\Projects\MindAttic\Prose" };
        ProjectCompanionService.EnsureStarted(project, project.Path);

        Assert.That(launchDir, Is.EqualTo(@"D:\Projects\MindAttic\Prose"));
    }

    [Test]
    public void EnsureStarted_detects_prose_from_working_directory_when_project_is_null()
    {
        var launched = false;
        ProjectCompanionService.HealthChecker = _ => false;
        ProjectCompanionService.ProcessLauncher = _ => { launched = true; };

        ProjectCompanionService.EnsureStarted(null, @"D:\Projects\MindAttic\Prose");

        Assert.That(launched, Is.True);
    }
}
