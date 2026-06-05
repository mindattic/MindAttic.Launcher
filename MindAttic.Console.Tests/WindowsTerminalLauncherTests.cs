using MindAttic.Console.Models;
using MindAttic.Console.Services;
using NUnit.Framework;

namespace MindAttic.Console.Tests;

[TestFixture]
public sealed class WindowsTerminalLauncherTests
{
    private static readonly Project Project = new() { Name = "Overlord", Path = @"D:\Projects\MindAttic" };
    private static readonly AgentProvider Provider = new() { Key = "Claude", Name = "Claude Code", RunCommand = "claude" };

    [Test]
    public void BuildAgentTab_appends_prompt_as_last_host_arg_when_provided()
    {
        var tab = new WindowsTerminalLauncher().BuildAgentTab(Project, Provider, "mindattic.exe", "tidy every repo");

        Assert.That(tab.Command, Does.Contain("--prompt"));
        // The order text must be the final argument so the host forwards it as
        // the agent's positional seed prompt.
        Assert.That(tab.Command[^2], Is.EqualTo("--prompt"));
        Assert.That(tab.Command[^1], Is.EqualTo("tidy every repo"));
    }

    [Test]
    public void BuildAgentTab_omits_prompt_when_null_or_blank()
    {
        var none  = new WindowsTerminalLauncher().BuildAgentTab(Project, Provider, "mindattic.exe");
        var blank = new WindowsTerminalLauncher().BuildAgentTab(Project, Provider, "mindattic.exe", "   ");

        Assert.That(none.Command, Does.Not.Contain("--prompt"));
        Assert.That(blank.Command, Does.Not.Contain("--prompt"));
    }
}
