using System.Diagnostics;
using MindAttic.Console.Models;

namespace MindAttic.Console.Services;

/// <summary>
/// Builds and invokes <c>wt</c> command lines. Centralises tab title / color /
/// scheme handling so menu code never quotes <c>wt</c> args by hand.
/// </summary>
public sealed class WindowsTerminalLauncher
{
    public sealed class Tab
    {
        public string? Title { get; init; }
        public string? WorkingDirectory { get; init; }
        public string? TabColor { get; init; }
        public string? ColorScheme { get; init; }
        public bool SuppressApplicationTitle { get; init; } = true;
        public required IReadOnlyList<string> Command { get; init; }
    }

    public void Open(Tab tab)
    {
        if (tab.Command.Count == 0)
            throw new ArgumentException("Tab.Command must contain at least the executable.", nameof(tab));

        var args = new List<string> { "-w", "0", "new-tab" };

        if (!string.IsNullOrWhiteSpace(tab.Title))
        {
            args.Add("--title");
            args.Add(tab.Title);
        }
        if (tab.SuppressApplicationTitle)
            args.Add("--suppressApplicationTitle");
        if (!string.IsNullOrWhiteSpace(tab.WorkingDirectory))
        {
            args.Add("-d");
            args.Add(tab.WorkingDirectory);
        }
        if (!string.IsNullOrWhiteSpace(tab.TabColor))
        {
            args.Add("--tabColor");
            args.Add(tab.TabColor);
        }
        if (!string.IsNullOrWhiteSpace(tab.ColorScheme))
        {
            args.Add("--colorScheme");
            args.Add(tab.ColorScheme);
        }

        args.Add("--");
        args.AddRange(tab.Command);

        var psi = new ProcessStartInfo("wt")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        // wt forks the new tab and exits immediately. Dispose the launcher
        // Process handle right away — callers were leaking it.
        using var launcher = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start wt.");
    }

    /// <summary>Builds an "agent host" tab — invokes `mindattic host …` for the given project + provider.</summary>
    public Tab BuildAgentTab(Project project, AgentProvider provider, string hostExePath)
    {
        var agentTitle = $"{project.TabTitle} [{provider.Key}]";
        return new Tab
        {
            Title = agentTitle,
            WorkingDirectory = project.Path,
            TabColor = project.TabColor,
            ColorScheme = project.ColorScheme,
            // TitlePinner toggles "Paused" into the tab title when the agent
            // isn't running; --suppressApplicationTitle would block those writes.
            SuppressApplicationTitle = false,
            Command =
            [
                hostExePath,
                "host",
                "--name", project.Name,
                "--title", agentTitle,
                "--provider", provider.Key
            ]
        };
    }

    public Tab BuildRunCommandTab(Project project)
    {
        // A blank RunCommand as `cmd /c ""` opens a tab that flashes and dies with
        // no explanation. Keep the pane open (`/k`) with a message instead so the
        // user can see why nothing ran — RunProjectMenu filters these out, but the
        // method is public and shouldn't manufacture a silently-broken tab.
        IReadOnlyList<string> command = string.IsNullOrWhiteSpace(project.RunCommand)
            ? ["cmd", "/k", $"echo No RunCommand configured for {project.Name}."]
            : ["cmd", "/c", project.RunCommand];
        return new Tab
        {
            Title = project.TabTitle,
            WorkingDirectory = project.Path,
            TabColor = project.TabColor,
            ColorScheme = project.ColorScheme,
            Command = command
        };
    }

    public Tab BuildCmdTab(string workingDirectory) => new()
    {
        Title = "Command Prompt",
        WorkingDirectory = workingDirectory,
        Command = ["cmd"]
    };

    /// <summary>
    /// Builds a tab that runs every MindAttic.Deploy category back-to-back
    /// via <c>cmd /k</c> so the pane stays open after the run for the user
    /// to read the per-batch summaries. <paramref name="commandLine"/> comes
    /// from <see cref="DeployService.BuildDeployAllCommandLine(string)"/>.
    /// </summary>
    public Tab BuildDeployAllTab(string workingDirectory, string commandLine) => new()
    {
        Title = "MindAttic.Deploy — all",
        WorkingDirectory = workingDirectory,
        Command = ["cmd", "/k", commandLine]
    };
}
