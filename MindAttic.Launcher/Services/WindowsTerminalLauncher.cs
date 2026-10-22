using System.Diagnostics;
using MindAttic.Launcher.Models;

namespace MindAttic.Launcher.Services;

/// <summary>
/// Builds and invokes <c>wt</c> command lines. Centralises tab title / color /
/// scheme handling so menu code never quotes <c>wt</c> args by hand.
/// <para>With an <see cref="AccountScope"/> (a Launcher run with <c>--account</c>, M.LNCH-T54) every Claude agent tab it
/// builds passes that login's folder to the host as <c>--config-dir</c> in the tab's own command (a wt tab doesn't inherit
/// this process's environment) and carries the login's alias in its title.</para>
/// </summary>
public sealed class WindowsTerminalLauncher(AccountScope? scope = null)
{
    public AccountScope? Scope { get; } = scope;

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

        var psi = new ProcessStartInfo("wt")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var a in BuildArgList(tab)) psi.ArgumentList.Add(a);

        // wt forks the new tab and exits immediately. Dispose the launcher
        // Process handle right away — callers were leaking it.
        using var launcher = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start wt.");
    }

    /// <summary>
    /// Like <see cref="Open"/> but launches <c>wt</c> elevated via UAC so the new
    /// tab runs as Administrator. If the user cancels the UAC prompt the call returns
    /// silently (Win32 error 1223 is swallowed).
    /// </summary>
    public void OpenElevated(Tab tab)
    {
        if (tab.Command.Count == 0)
            throw new ArgumentException("Tab.Command must contain at least the executable.", nameof(tab));

        var psi = new ProcessStartInfo("wt")
        {
            UseShellExecute = true,
            Verb = "runas"
        };
        foreach (var a in BuildArgList(tab)) psi.ArgumentList.Add(a);

        try { using var _ = Process.Start(psi); }
        catch (System.ComponentModel.Win32Exception) { /* user cancelled UAC */ }
    }

    private static IEnumerable<string> BuildArgList(Tab tab)
    {
        yield return "-w"; yield return "0"; yield return "new-tab";
        if (!string.IsNullOrWhiteSpace(tab.Title))
            { yield return "--title"; yield return tab.Title; }
        if (tab.SuppressApplicationTitle)
            yield return "--suppressApplicationTitle";
        if (!string.IsNullOrWhiteSpace(tab.WorkingDirectory))
            { yield return "-d"; yield return tab.WorkingDirectory; }
        if (!string.IsNullOrWhiteSpace(tab.TabColor))
            { yield return "--tabColor"; yield return tab.TabColor; }
        if (!string.IsNullOrWhiteSpace(tab.ColorScheme))
            { yield return "--colorScheme"; yield return tab.ColorScheme; }
        yield return "--";
        foreach (var c in tab.Command) yield return c;
    }

    /// <summary>
    /// Builds an "agent host" tab — invokes `mindattic host …` for the given
    /// project + provider. When <paramref name="prompt"/> is set it's passed
    /// through as <c>--prompt</c> so the agent opens with that text pre-loaded.
    /// </summary>
    public Tab BuildAgentTab(Project project, AgentProvider provider, string hostExePath, string? prompt = null)
    {
        var agentTitle = ScopedTitle($"{project.TabTitle} [{provider.Key}]", provider);
        var command = new List<string>
        {
            hostExePath,
            "host",
            "--name", project.Name,
            "--title", agentTitle,
            "--provider", provider.Key
        };
        AddScopeAndPrompt(command, provider, prompt);

        return new Tab
        {
            Title = agentTitle,
            WorkingDirectory = project.Path,
            TabColor = project.TabColor,
            ColorScheme = project.ColorScheme,
            // TitlePinner toggles "Paused" into the tab title when the agent
            // isn't running; --suppressApplicationTitle would block those writes.
            SuppressApplicationTitle = false,
            Command = command
        };
    }

    /// <summary>
    /// Builds an "agent host" tab rooted at an arbitrary directory rather than a
    /// registered project — invokes `mindattic host --path …` (e.g. the Status
    /// tab, rooted at the whole MindAttic workspace). When <paramref name="prompt"/>
    /// is set it's forwarded as <c>--prompt</c> so the session opens with that
    /// text pre-loaded.
    /// </summary>
    public Tab BuildAgentTabAtPath(
        string title, string workingDirectory, AgentProvider provider, string hostExePath,
        string? tabColor = null, string? colorScheme = null, string? prompt = null)
    {
        title = ScopedTitle(title, provider);
        var command = new List<string>
        {
            hostExePath,
            "host",
            "--path", workingDirectory,
            "--title", title,
            "--provider", provider.Key
        };
        AddScopeAndPrompt(command, provider, prompt);

        return new Tab
        {
            Title = title,
            WorkingDirectory = workingDirectory,
            TabColor = tabColor,
            ColorScheme = colorScheme,
            // Match BuildAgentTab: let TitlePinner write "Paused" into the title.
            SuppressApplicationTitle = false,
            Command = command
        };
    }

    private static bool IsClaude(AgentProvider provider) =>
        string.Equals(provider.Key, "Claude", StringComparison.OrdinalIgnoreCase);

    /// <summary><paramref name="title"/>, stamped with the scope's alias for a Claude tab of a scoped Launcher.</summary>
    private string ScopedTitle(string title, AgentProvider provider) =>
        Scope is { } s && IsClaude(provider) ? s.Stamp(title) : title;

    /// <summary>Adds the scope's <c>--config-dir</c> (Claude tabs of a scoped Launcher), then the prompt, which must stay
    /// the host's last argument.</summary>
    private void AddScopeAndPrompt(List<string> command, AgentProvider provider, string? prompt)
    {
        if (Scope is { } s && IsClaude(provider))
        {
            command.Add("--config-dir");
            command.Add(s.ConfigDir);
        }
        if (!string.IsNullOrWhiteSpace(prompt))
        {
            command.Add("--prompt");
            command.Add(prompt);
        }
    }

    public Tab BuildRunCommandTab(Project project)
    {
        // A blank RunCommand as `cmd /c ""` opens a tab that flashes and dies with
        // no explanation. Keep the pane open (`/k`) with a message instead so the
        // user can see why nothing ran. ProjectActionMenu is the only caller, but the
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
        Title = "Command Prompt (Administrator)",
        WorkingDirectory = workingDirectory,
        Command = ["cmd"]
    };

    public Tab BuildPowerShellTab(string workingDirectory) => new()
    {
        Title = "PowerShell (Administrator)",
        WorkingDirectory = workingDirectory,
        Command = ["powershell"]
    };
}
