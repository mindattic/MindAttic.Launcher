using MindAttic.Launcher.Services;
using NUnit.Framework;

namespace MindAttic.Launcher.Tests;

[TestFixture]
public sealed class DeployServiceTests
{
    private const string ConsoleRoot = @"D:\Projects\MindAttic\MindAttic.Launcher";
    private const string ExpectedExe = @"D:\Projects\MindAttic\MindAttic.Deploy\artifacts\MindAttic.Deploy.exe";

    [Test]
    public void ResolveExe_returns_sibling_path_when_artifact_present()
    {
        var subject = new DeployService(exists: p =>
            string.Equals(p, ExpectedExe, StringComparison.OrdinalIgnoreCase));

        Assert.That(subject.ResolveExe(ConsoleRoot), Is.EqualTo(ExpectedExe));
    }

    [Test]
    public void ResolveExe_returns_null_when_artifact_missing()
    {
        var subject = new DeployService(exists: _ => false);

        Assert.That(subject.ResolveExe(ConsoleRoot), Is.Null);
    }

    [Test]
    public void ResolveExe_returns_null_for_blank_root()
    {
        var subject = new DeployService(exists: _ => true);

        Assert.That(subject.ResolveExe(""), Is.Null);
    }

    [Test]
    public void BuildDeployAllCommandLine_delegates_to_cli_all_subcommand()
    {
        // Deploy CLI's `all` sub-command handles cross-batch sequencing and
        // failure tallying internally — Console just invokes it.
        var cmd = DeployService.BuildDeployAllCommandLine(ExpectedExe);

        Assert.That(cmd, Is.EqualTo($"\"{ExpectedExe}\" all"));
    }
}
