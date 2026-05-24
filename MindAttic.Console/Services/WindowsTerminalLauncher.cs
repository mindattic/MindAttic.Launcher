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
        return new Tab
        {
            Title = project.TabTitle,
            WorkingDirectory = project.Path,
            TabColor = project.TabColor,
            ColorScheme = project.ColorScheme,
            Command = ["cmd", "/c", project.RunCommand ?? ""]
        };
    }

    public Tab BuildCmdTab(string workingDirectory) => new()
    {
        Title = "Command Prompt",
        WorkingDirectory = workingDirectory,
        Command = ["cmd"]
    };
}
