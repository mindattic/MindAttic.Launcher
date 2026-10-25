using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MindAttic.Launcher.Models;

namespace MindAttic.Launcher.Services;

/// <summary>
/// Builds and invokes <c>wt</c> command lines. Centralises tab title / color /
/// scheme handling so menu code never quotes <c>wt</c> args by hand.
/// <para>With an <see cref="AccountScope"/> (a Launcher run with <c>--account</c>, M.LNCH-T54) every Claude agent tab it
/// builds passes that login's folder to the host as <c>--config-dir</c> in the tab's own command (a wt tab doesn't inherit
/// this process's environment).</para>
/// <para>Every Claude agent tab whose login is known when it opens goes in that person's own window (M.LNCH-T55, the
/// Orchestrator's rule, M.ORCH-T616): <c>wt -w &lt;alias&gt;</c>, its title badged with the login's initials
/// ("RD: Repo [Claude]") and its tab colored as the login, all read from <c>mindattic-account.json</c> in the login's
/// folder (<see cref="LoginLook"/>). A scoped Launcher's login is its scope's, decided once at startup — either
/// <c>--account</c> (M.LNCH-T54) or, without it, the one-time question <c>WhoIsUsing</c> asks from the org's current,
/// eligible members (M.LNCH-T71, which replaced an earlier per-tab question, M.LNCH-T65); an unscoped Launcher's is the
/// global login.</para>
/// <para>Such a tab also opens with the login's own Terminal profile (M.LNCH-T56, <see cref="LoginTerminalProfiles"/>), whose
/// icon is the login's initials on its tab color, so whose tab it is shows as an icon too. Building a tab only names the
/// profile; <see cref="Open"/> writes its fragment and icon, so tests that build tabs never touch the fragments folder.</para>
/// <para>When this process itself is one of the Orchestrator's own embedded Terminals-panel panes — its wrapper sets
/// <see cref="OrchestratorTerminalsEnvVar"/> to a named pipe — opening an agent-host tab (<see cref="Tab.IsAgentHost"/>)
/// routes into that panel as a new tab there instead of its own <c>wt</c> window (M.LNCH-T77). Nothing listening on the
/// pipe (the dashboard closed since) falls back to <c>wt</c> exactly as if unembedded; a utility tab (elevated, or built
/// by <see cref="OpenElevated"/>) always still opens its own real window.</para>
/// </summary>
public sealed class WindowsTerminalLauncher(
    AccountScope? scope = null, Func<LoginLook?>? claudeLook = null, LoginTerminalProfiles? profiles = null,
    Func<string, LoginLook>? loginLook = null, Func<string, string, string?>? sendToOrchestrator = null,
    Action<WindowsTerminalLauncher.Tab>? launchWindowsTerminal = null)
{
    /// <summary>Set by the Orchestrator in every pane it hosts in its embedded Terminals panel, including this
    /// process's own menu when opened that way; its value is the named pipe an agent-host tab routes into (M.LNCH-T77).</summary>
    public const string OrchestratorTerminalsEnvVar = "MINDATTIC_ORCHESTRATOR_TERMINALS";

    /// <summary>Sends an agent-host tab's request down the Orchestrator's Terminals pipe. Null means nothing is
    /// listening (fall back to <c>wt</c>); otherwise the dashboard's reply line — blank for success, non-blank its
    /// error. Tests fake this; production goes through <see cref="SendToOrchestratorPipe"/>.</summary>
    private readonly Func<string, string, string?> sendToOrchestrator = sendToOrchestrator ?? SendToOrchestratorPipe;

    /// <summary>Actually starts <c>wt</c> for a tab <see cref="Open"/> decided isn't going down the Orchestrator's pipe.
    /// A seam purely so tests can assert "it fell back to wt" without a real Windows Terminal popping up; production
    /// goes through <see cref="LaunchWindowsTerminal"/>, unchanged from before M.LNCH-T77.</summary>
    private readonly Action<Tab> launchWindowsTerminal = launchWindowsTerminal ?? LaunchWindowsTerminal;

    public AccountScope? Scope { get; } = scope;

    /// <summary>Where Claude logins' Terminal profiles are written as their tabs open; tests pass a temp folder's.</summary>
    private readonly LoginTerminalProfiles? profiles = profiles;
    private LoginTerminalProfiles Profiles => profiles ?? (defaultProfiles ??= new LoginTerminalProfiles());
    private LoginTerminalProfiles? defaultProfiles;

    /// <summary>The look of a login chosen in the Launcher (a config dir, "" = the global login). Tests pass their own.</summary>
    private readonly Func<string, LoginLook> loginLook = loginLook ?? (dir => LoginLook.ForConfigDir(dir));

    /// <summary>The window, badge and color of the login a Claude tab opened now runs on; null when that isn't known until
    /// the tab asks. Tests pass their own rather than read the real ~/.claude.</summary>
    private readonly Func<LoginLook?> claudeLook = claudeLook ?? (() => DefaultClaudeLook(scope));

    /// <summary>The login a Claude tab of this Launcher runs on, as it opens: the scope's, else the global login when no
    /// per-CLI login exists (the tab won't ask), else unknown.</summary>
    public static LoginLook? DefaultClaudeLook(AccountScope? scope, string? accountsRoot = null, string? userProfile = null)
    {
        if (scope is not null) return LoginLook.ForConfigDir(scope.ConfigDir);
        if (ClaudeAccountCatalog.PerCliLogins(accountsRoot).Count > 0) return null;
        return LoginLook.ForConfigDir(null, userProfile);
    }

    /// <summary>The window a scoped Launcher's own tabs (its restart) go in: its login's; null (the current one) unscoped.</summary>
    public string? ScopeWindow => Scope is null ? null : claudeLook()?.Window;

    public sealed record Tab
    {
        /// <summary>The Windows Terminal window the tab opens in (<c>wt -w</c>); null or blank: the current one ("0").</summary>
        public string? Window { get; init; }
        /// <summary>The Terminal profile the tab opens with (<c>wt new-tab -p</c>); null: Terminal's default.</summary>
        public string? Profile { get; init; }
        /// <summary>The login whose profile <see cref="Profile"/> is; <see cref="Open"/> writes that profile first.</summary>
        public LoginLook? ProfileLook { get; init; }
        public string? Title { get; init; }
        public string? WorkingDirectory { get; init; }
        public string? TabColor { get; init; }
        public string? ColorScheme { get; init; }
        public bool SuppressApplicationTitle { get; init; } = true;
        public required IReadOnlyList<string> Command { get; init; }

        /// <summary>Only an agent CLI tab (<see cref="BuildAgentTab"/>/<see cref="BuildAgentTabAtPath"/>) can route into
        /// the Orchestrator's embedded Terminals panel (M.LNCH-T77) — a utility tab (Command Prompt, PowerShell) always
        /// opens its own real <c>wt</c> window, embedded or not.</summary>
        public bool IsAgentHost { get; init; }

        /// <summary>This tab's <see cref="AgentProvider.Key"/> ("Claude", "Codex", …); cosmetic/routing only, read when
        /// this is an agent-host tab sent down the Orchestrator's pipe.</summary>
        public string? Provider { get; init; }

        /// <summary>The project this tab is for, if it's rooted at one (<see cref="BuildAgentTab"/>); null for a tab
        /// rooted at an arbitrary path (<see cref="BuildAgentTabAtPath"/>), where naming one isn't cheap to get right.</summary>
        public string? Repo { get; init; }
    }

    public void Open(Tab tab)
    {
        if (tab.Command.Count == 0)
            throw new ArgumentException("Tab.Command must contain at least the executable.", nameof(tab));

        // Embedded in the Orchestrator's own Terminals panel (M.LNCH-T77): an agent-host tab goes down that pipe as a
        // new tab there instead of its own wt window. Nothing listening (null — e.g. the dashboard closed since this
        // Launcher was embedded) falls back to wt below, exactly as if the env var had never been set. A reply means
        // the dashboard took the request either way — a blank line is success, a non-blank one its error; either way
        // we must NOT also open a wt tab here, or the request would be duplicated.
        if (tab.IsAgentHost && Environment.GetEnvironmentVariable(OrchestratorTerminalsEnvVar) is { Length: > 0 } pipeName)
        {
            var reply = sendToOrchestrator(pipeName, PipeRequestJson(tab));
            if (reply is not null)
            {
                if (reply.Length > 0)
                    throw new InvalidOperationException($"Failed to open tab via the Orchestrator: {reply}");
                return;
            }
        }

        launchWindowsTerminal(WithProfileWritten(tab));
    }

    private static void LaunchWindowsTerminal(Tab tab)
    {
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

        tab = WithProfileWritten(tab);
        var psi = new ProcessStartInfo("wt")
        {
            UseShellExecute = true,
            Verb = "runas"
        };
        foreach (var a in BuildArgList(tab)) psi.ArgumentList.Add(a);

        try { using var _ = Process.Start(psi); }
        catch (System.ComponentModel.Win32Exception) { /* user cancelled UAC */ }
    }

    /// <summary><paramref name="tab"/> once its login's profile is written (or rewritten after a rename); without its
    /// profile, as Terminal's default, when that can't be written.</summary>
    public Tab WithProfileWritten(Tab tab)
    {
        if (tab.ProfileLook is not { } look) return tab;
        return Profiles.Ensure(look) is { } guid ? tab with { Profile = guid } : tab with { Profile = null, ProfileLook = null };
    }

    public static IEnumerable<string> BuildArgList(Tab tab)
    {
        yield return "-w"; yield return string.IsNullOrWhiteSpace(tab.Window) ? "0" : tab.Window; yield return "new-tab";
        if (!string.IsNullOrWhiteSpace(tab.Profile))
            { yield return "-p"; yield return tab.Profile; }
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

    private static readonly JsonSerializerOptions PipeRequestOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>The wire shape the Orchestrator's own tests already exercise for a Terminals-panel tab request
    /// (M.LNCH-T77) — field names are case-insensitive on its end, but kept camelCase here to match exactly.</summary>
    private sealed record PipeTabRequest
    {
        public required string Title { get; init; }
        public required string Directory { get; init; }
        public required IReadOnlyList<string> Command { get; init; }
        public string? Repo { get; init; }
        public string? Provider { get; init; }
        public string? ConfigDir { get; init; }
        public string? Email { get; init; }
        public string? Initials { get; init; }
        public string? TabColor { get; init; }
    }

    /// <summary>One line of UTF-8 JSON for <paramref name="tab"/>, exactly the <c>wt</c> command it would otherwise
    /// run — same title, directory and argv, different transport. The login's email/initials come from
    /// <see cref="Tab.ProfileLook"/> (already resolved for this tab's window/profile/color); its config folder isn't
    /// kept on <see cref="Tab"/> separately, so it's read back out of the command this tab already carries, the same
    /// <c>--config-dir</c> <see cref="AddLoginAndPrompt"/> added.</summary>
    private static string PipeRequestJson(Tab tab) => JsonSerializer.Serialize(new PipeTabRequest
    {
        Title = tab.Title ?? "",
        Directory = tab.WorkingDirectory ?? "",
        Command = tab.Command,
        Repo = tab.Repo,
        Provider = tab.Provider,
        ConfigDir = ConfigDirFromCommand(tab.Command),
        Email = tab.ProfileLook?.Email,
        Initials = tab.ProfileLook?.Initials,
        TabColor = tab.TabColor
    }, PipeRequestOptions);

    private static string? ConfigDirFromCommand(IReadOnlyList<string> command)
    {
        for (var i = 0; i < command.Count - 1; i++)
            if (command[i] == "--config-dir") return command[i + 1];
        return null;
    }

    /// <summary>Round-trips <paramref name="json"/> over the named pipe the Orchestrator told us about: connects (~1s),
    /// writes the one request line, reads the one reply line (~30s). Null — connect timed out, or nothing is listening
    /// (<see cref="IOException"/>/<see cref="UnauthorizedAccessException"/>, e.g. the dashboard closed) — tells
    /// <see cref="Open"/> to fall back to <c>wt</c>; any other outcome is the dashboard's own reply.</summary>
    private static string? SendToOrchestratorPipe(string pipeName, string json)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.CurrentUserOnly);
            pipe.Connect(1000);

            // leaveOpen on both: they share one pipe, and a Reader/Writer's Dispose otherwise closes the underlying
            // stream — the first of the pair to dispose would pull it out from under the other.
            using var writer = new StreamWriter(pipe, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true) { NewLine = "\n", AutoFlush = true };
            writer.WriteLine(json);

            using var reader = new StreamReader(pipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true);
            var readLine = reader.ReadLineAsync();
            return readLine.Wait(TimeSpan.FromSeconds(30)) ? readLine.Result ?? "" : "timed out waiting for the Orchestrator's reply";
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Builds an "agent host" tab — invokes `mindattic host …` for the given
    /// project + provider. When <paramref name="prompt"/> is set it's passed
    /// through as <c>--prompt</c> so the agent opens with that text pre-loaded.
    /// <paramref name="login"/> is an explicit login to open on, outside the usual scope (a config dir, "" for the
    /// global login, null for none); a scope wins over it, and other providers ignore it.
    /// </summary>
    public Tab BuildAgentTab(Project project, AgentProvider provider, string hostExePath, string? prompt = null, string? login = null)
    {
        var look = LookFor(provider, login);
        var agentTitle = look?.Badge($"{project.TabTitle} [{provider.Key}]") ?? $"{project.TabTitle} [{provider.Key}]";
        var command = new List<string>
        {
            hostExePath,
            "host",
            "--name", project.Name,
            "--title", agentTitle,
            "--provider", provider.Key
        };
        AddLoginAndPrompt(command, provider, prompt, login);

        return new Tab
        {
            Window = look?.Window,
            Profile = look is null ? null : LoginTerminalProfiles.ProfileGuid(look),
            ProfileLook = look,
            Title = agentTitle,
            WorkingDirectory = project.Path,
            TabColor = look?.TabColor ?? project.TabColor,
            ColorScheme = project.ColorScheme,
            // TitlePinner toggles "Paused" into the tab title when the agent
            // isn't running; --suppressApplicationTitle would block those writes.
            SuppressApplicationTitle = false,
            Command = command,
            IsAgentHost = true,
            Provider = provider.Key,
            Repo = project.Name
        };
    }

    /// <summary>
    /// Builds an "agent host" tab rooted at an arbitrary directory rather than a
    /// registered project — invokes `mindattic host --path …` (e.g. the Status
    /// tab, rooted at the whole MindAttic workspace). When <paramref name="prompt"/>
    /// is set it's forwarded as <c>--prompt</c> so the session opens with that
    /// text pre-loaded. <paramref name="login"/> as for <see cref="BuildAgentTab"/>.
    /// </summary>
    public Tab BuildAgentTabAtPath(
        string title, string workingDirectory, AgentProvider provider, string hostExePath,
        string? tabColor = null, string? colorScheme = null, string? prompt = null, string? login = null)
    {
        var look = LookFor(provider, login);
        title = look?.Badge(title) ?? title;
        var command = new List<string>
        {
            hostExePath,
            "host",
            "--path", workingDirectory,
            "--title", title,
            "--provider", provider.Key
        };
        AddLoginAndPrompt(command, provider, prompt, login);

        return new Tab
        {
            Window = look?.Window,
            Profile = look is null ? null : LoginTerminalProfiles.ProfileGuid(look),
            ProfileLook = look,
            Title = title,
            WorkingDirectory = workingDirectory,
            TabColor = look?.TabColor ?? tabColor,
            ColorScheme = colorScheme,
            // Match BuildAgentTab: let TitlePinner write "Paused" into the title.
            SuppressApplicationTitle = false,
            Command = command,
            IsAgentHost = true,
            Provider = provider.Key
            // No Repo: a path-rooted tab isn't for any one registered project.
        };
    }

    internal static bool IsClaude(AgentProvider provider) =>
        string.Equals(provider.Key, "Claude", StringComparison.OrdinalIgnoreCase);

    /// <summary>The look of a Claude tab's login (read afresh, so a rename shows at once); null for other providers, which
    /// have no login, and while the login isn't known yet.</summary>
    private LoginLook? LookFor(AgentProvider provider, string? login) =>
        !IsClaude(provider) ? null
        : Scope is null && login is not null ? loginLook(login)
        : claudeLook();

    /// <summary>The login folder a Claude tab's host is told to run under: the scope's, else the one asked for (the global
    /// login as its own folder, <c>~/.claude</c>, which the host maps back to the global login), else none.</summary>
    private string? ConfigDirFor(AgentProvider provider, string? login) =>
        !IsClaude(provider) ? null
        : Scope is { } s ? s.ConfigDir
        : login is null ? null
        : login.Length == 0 ? ClaudeResume.GlobalConfigDir
        : login;

    /// <summary>Adds the tab's <c>--config-dir</c> (a Claude tab whose login is decided before it opens), then the prompt,
    /// which must stay the host's last argument.</summary>
    private void AddLoginAndPrompt(List<string> command, AgentProvider provider, string? prompt, string? login)
    {
        if (ConfigDirFor(provider, login) is { } dir)
        {
            command.Add("--config-dir");
            command.Add(dir);
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
