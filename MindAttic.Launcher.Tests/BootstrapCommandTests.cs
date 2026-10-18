using MindAttic.Launcher.Commands;
using NUnit.Framework;

namespace MindAttic.Launcher.Tests;

[TestFixture]
public sealed class BootstrapCommandTests
{
    private static BootstrapCommand.Phases Recording(List<string> order, string? failing = null)
    {
        Func<bool> Phase(string name) => () => { order.Add(name); return name != failing; };
        return new BootstrapCommand.Phases(
            Prerequisites: Phase("prerequisites"),
            OptionalTools: Phase("optional"),
            AiClis: Phase("ai-clis"),
            RestoreAppData: Phase("appdata"),
            Repos: Phase("repos"),
            StartLocalDb: Phase("localdb"),
            RestoreDatabases: Phase("databases"));
    }

    [Test]
    public void AppData_is_restored_before_the_repos_so_the_GitHub_token_it_holds_can_find_them()
    {
        var order = new List<string>();

        var ok = BootstrapCommand.RunPhases(Recording(order));

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.True);
            Assert.That(order, Is.EqualTo(new[]
            {
                "prerequisites", "optional", "ai-clis", "appdata", "repos", "localdb", "databases"
            }));
        });
    }

    [Test]
    public void LocalDB_is_started_before_the_databases_are_restored()
    {
        var order = new List<string>();

        BootstrapCommand.RunPhases(Recording(order));

        Assert.That(order.IndexOf("localdb"), Is.LessThan(order.IndexOf("databases")));
    }

    [Test]
    public void A_failing_phase_fails_bootstrap_but_never_skips_the_rest()
    {
        var order = new List<string>();

        var ok = BootstrapCommand.RunPhases(Recording(order, failing: "repos"));

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.False);
            Assert.That(order, Has.Count.EqualTo(7));
        });
    }
}
