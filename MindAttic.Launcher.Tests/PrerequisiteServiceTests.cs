using MindAttic.Launcher.Services;
using NUnit.Framework;

namespace MindAttic.Launcher.Tests;

[TestFixture]
public sealed class PrerequisiteServiceTests
{
    [Test]
    public void BuildWingetInstallArguments_is_a_silent_unattended_install()
    {
        Assert.That(PrerequisiteService.BuildWingetInstallArguments("Microsoft.sqlcmd"),
            Is.EqualTo(new[]
            {
                "install", "--id", "Microsoft.sqlcmd", "-e", "--silent",
                "--accept-package-agreements", "--accept-source-agreements"
            }));
    }

    [Test]
    public void EnsureHard_skips_install_when_the_command_already_exists()
    {
        var installed = false;
        var subject = new PrerequisiteService(
            exists: _ => true,
            run: (_, _, _) => { installed = true; return (0, ""); });

        var result = subject.EnsureHard("sqlcmd", ["Microsoft.sqlcmd"]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Present, Is.True);
            Assert.That(result.Installed, Is.False);
            Assert.That(installed, Is.False);
        });
    }

    [Test]
    public void EnsureHard_installs_via_the_first_winget_id_that_succeeds()
    {
        var tried = new List<string>();
        var subject = new PrerequisiteService(
            exists: _ => false,
            run: (_, args, _) =>
            {
                tried.Add(args[2]); // --id <value>
                return (0, "");
            });

        var result = subject.EnsureHard("sqllocaldb", ["Microsoft.SQLServer.2022.LocalDB", "Microsoft.SQLServer.2019.LocalDB"]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Present, Is.True);
            Assert.That(result.Installed, Is.True);
            Assert.That(tried, Is.EqualTo(new[] { "Microsoft.SQLServer.2022.LocalDB" }));
        });
    }

    [Test]
    public void EnsureHard_falls_through_to_the_next_winget_id_when_the_first_fails()
    {
        var tried = new List<string>();
        var subject = new PrerequisiteService(
            exists: _ => false,
            run: (_, args, _) =>
            {
                var id = args[2];
                tried.Add(id);
                return id == "Microsoft.SQLServer.2022.LocalDB" ? (1, "not found") : (0, "");
            });

        var result = subject.EnsureHard("sqllocaldb", ["Microsoft.SQLServer.2022.LocalDB", "Microsoft.SQLServer.2019.LocalDB"]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Present, Is.True);
            Assert.That(tried, Is.EqualTo(new[] { "Microsoft.SQLServer.2022.LocalDB", "Microsoft.SQLServer.2019.LocalDB" }));
        });
    }

    [Test]
    public void EnsureHard_reports_failure_when_every_winget_id_fails()
    {
        var subject = new PrerequisiteService(
            exists: _ => false,
            run: (_, _, _) => (1, "package not found"));

        var result = subject.EnsureHard("sqlcmd", ["Microsoft.sqlcmd"]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Present, Is.False);
            Assert.That(result.Error, Does.Contain("sqlcmd"));
        });
    }
}
