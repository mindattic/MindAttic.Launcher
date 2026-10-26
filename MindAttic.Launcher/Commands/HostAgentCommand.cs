using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using MindAttic.Launcher.Interop;
using MindAttic.Launcher.Models;
using MindAttic.Launcher.Services;
using Spectre.Console.Cli;

namespace MindAttic.Launcher.Commands;

/// <summary>
/// Per-tab agent host: resolves where to root the agent (a registered project by
/// <c>--name</c>, or any directory by <c>--path</c> — the latter is how the
/// Status tab roots a session at the MindAttic workspace root), splits the
/// provider's RunCommand into argv, sets the tab title and starts the
/// title-pinner, then execs the agent with inherited stdio.
/// </summary>
[SuppressMessage("Performance", "CA1812", Justification = "Instantiated by Spectre.Console.Cli")]
public sealed class HostAgentCommand : Command<HostAgentCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("--name <NAME>")]
        [Description("Project name as listed in settings.json. Optional when --path is given.")]
        public string Name { get; init; } = "";

        [CommandOption("--path <PATH>")]
        [Description("Root the agent at this directory instead of a registered project (e.g. the Status tab uses the MindAttic workspace root). Takes precedence over --name.")]
        public string? Path { get; init; }

        [CommandOption("--title <TITLE>")]
        [Description("Tab title (defaults to --name).")]
        public string? Title { get; init; }

        [CommandOption("--provider <PROVIDER>")]
        [Description("Override the project's configured agent provider.")]
        public string? Provider { get; init; }

        [CommandOption("--prompt <PROMPT>")]
        [Description("Seed the agent's first turn with this text (e.g. /status). Pre-fills the input; the CLI does not auto-submit.")]
        public string? Prompt { get; init; }

        [CommandOption("--config-dir <DIR>")]
        [Description("Run a Claude tab's CLI under this login folder (CLAUDE_CONFIG_DIR). A Launcher scoped to one account (--account) puts it in every Claude tab's command, since Windows Terminal tabs don't inherit the Launcher's environment, and an unscoped one puts the login it asked for there; the global login's own folder (~/.claude) names the global login. Skips the account question.")]
        public string? ConfigDir { get; init; }

        [CommandOption("--account <EMAIL>")]
        [Description("Run a Claude tab's CLI under this signed-in login (a ~/.claude-accounts/<email> folder). Skips the account question; an email that isn't a signed-in per-CLI login is an error.")]
        public string? Account { get; init; }

        [CommandOption("--resume <SESSION>")]
        [Description("Start a Claude tab by resuming this conversation (claude --resume <SESSION>) instead of a fresh one; --prompt is ignored. Without --config-dir/--account it runs under the signed-in login whose folder holds the session.")]
        public string? Resume { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var store = new SettingsStore();
        var registry = new AgentProviderRegistry(store);

        // Two ways to root the agent. --path wins: it hosts a session at an
        // arbitrary directory with no roster entry (e.g. a General tab, rooted
        // at the whole MindAttic workspace). Otherwise --name looks a registered
        // project up the usual way.
        var root = ResolveRoot(settings.Path, settings.Name, n => ProjectRoster.FindByName(store.Load(), n));
        if (root.Error is not null)
        {
            System.Console.Error.WriteLine(root.Error);
            return root.ExitCode;
        }
        var project = root.Project;
        var workingDir = root.WorkingDir!;

        // An explicit --provider that doesn't resolve is a caller bug (a typo),
        // not "quietly launch the default" - fail loudly instead. With none, a
        // direct `host` call resolves to the first-listed provider.
        var resolved = ResolveProvider(settings.Provider, registry);
        if (resolved.Error is not null)
        {
            System.Console.Error.WriteLine(resolved.Error);
            return resolved.ExitCode;
        }
        var provider = resolved.Provider!;

        var title = ResolveTitle(settings.Title, settings.Name, workingDir);

        var command = ResolveCommand(provider);
        if (command.Error is not null)
        {
            System.Console.Error.WriteLine(command.Error);
            return command.ExitCode;
        }
        var parts = command.Parts!;

        // Claude tabs also take the Orchestrator's restart-resume requests on a
        // control pipe of their own (its billing switch, M.LNCH-T38): the CLI is
        // restarted with --resume at its next idle point, billing the new mode.
        var isClaude = string.Equals(provider.Key, "Claude", StringComparison.OrdinalIgnoreCase);

        // --resume (M.LNCH-T64): the Orchestrator reopening an embedded pane after a dashboard restart starts
        // the tab on that conversation rather than a fresh one, still inside this host.
        var resumeSession = settings.Resume?.Trim();
        if (ClaudeResume.Validate(provider.Key, settings.Resume) is { } resumeError)
        {
            System.Console.Error.WriteLine(resumeError);
            return 64;
        }

        // The login this tab's Claude runs under (CLAUDE_CONFIG_DIR, "" = global). A restart-resume
        // with a configDir moves it (M.LNCH-T52), and every later start keeps the moved one. Before
        // that, a Claude tab asks which signed-in login to start on when more than one is around
        // (M.LNCH-T53): --account names one outright, a single login skips the question, and an
        // interactive tab offers the full catalog with the Orchestrator's Auto pick preselected.
        // An explicit --config-dir (a Launcher scoped to one account up front, M.LNCH-T54/M.LNCH-T71) wins
        // over whatever was inherited and skips the question; the global login's own folder (~/.claude)
        // names the global login.
        var loginGiven = !string.IsNullOrWhiteSpace(settings.ConfigDir);
        var claudeConfigDir = loginGiven
            ? ClaudeLogin.FromOption(settings.ConfigDir!)
            : ClaudeLogin.Initial(Environment.GetEnvironmentVariable(ClaudeLogin.ConfigDirVariable));
        var showAccountInTitle = false;
        if (isClaude)
        {
            if (!string.IsNullOrWhiteSpace(settings.Account))
            {
                var picked = ClaudeAccountResolver.Resolve(settings.Account, interactive: false);
                if (picked.Error is not null)
                {
                    System.Console.Error.WriteLine(picked.Error);
                    return 5;
                }
                claudeConfigDir = picked.ConfigDir;
                showAccountInTitle = picked.ShowInTitle;
            }
            else if (!loginGiven
                     && string.IsNullOrWhiteSpace(claudeConfigDir)
                     && resumeSession is not null
                     && ClaudeResume.FindConfigDir(resumeSession, ClaudeResume.SignedInLogins()) is { } sessionLogin)
            {
                // A resumed conversation belongs to the login that wrote it; asking would only let the
                // tab pick one where `claude --resume` can't find it.
                claudeConfigDir = sessionLogin;
                showAccountInTitle = ClaudeAccountCatalog.PerCliLogins().Count > 0;
            }
            else if (!loginGiven && string.IsNullOrWhiteSpace(claudeConfigDir))
            {
                var picked = ClaudeAccountResolver.Resolve(
                    requestedEmail: null,
                    interactive: !System.Console.IsInputRedirected,
                    choose: (options, auto) => ClaudeAccountPrompt.Ask(options, auto));
                claudeConfigDir = picked.ConfigDir;
                showAccountInTitle = picked.ShowInTitle;
            }
            if (showAccountInTitle)
                title = $"{title} — {ClaudeLogin.Describe(claudeConfigDir)}";
        }

        PullBeforeLaunch(workingDir, title);

        using var pinner = new TitlePinner(title);
        // Per-tab pipe lets text (e.g. /remote-control, via
        // RemoteControlBroadcaster) be typed into every running Claude/Codex
        // tab through ConsoleInputInjector. Started before Process.Start so a broadcast
        // landing during agent startup isn't missed.
        using var inputPipe = new HostInputPipeServer(provider.Key);

        // The billing this CLI keeps on top of the mode: an emergency failover's (M.LNCH-T67), until another request says otherwise.
        string? claudeBilling = null;
        RestartResumeRequest? pending = null;
        var pendingLock = new object();
        using var controlPipe = isClaude
            ? HostControlPipeServer.ForProvider(provider.Key, r => { lock (pendingLock) pending = r; })
            : null;
        // Marks this login as "has a CLI" for any later account picker in another tab
        // (M.LNCH-T53); best-effort only — never blocks the launch when it can't be claimed.
        using var loginMarker = isClaude ? new ClaudeLoginMarker(claudeConfigDir) : null;

        ProcessStartInfo BuildStartInfo(string? resumeSession)
        {
            // Bare npm-shim names (codex, gemini, ...) have no .exe on PATH, and
            // CreateProcessW (what Process.Start uses below) doesn't walk PATHEXT
            // the way cmd.exe does — so resolve to the actual .cmd/.bat shim first.
            var psi = BuildBaseStartInfo(parts, workingDir, resumeSession, settings.Prompt);

            // Push this provider's API key (if Vault has one) to wherever its CLI
            // expects it — an env var for Gemini, config.toml for Kimi. A missing
            // Vault entry is a no-op; the CLI falls back to its own configured auth.
            // Claude follows the Orchestrator's billing mode (ClaudeBillingMode).
            // A login folder that is the Vault API key's own identity (M.ORCH-T906) bills the key whatever the mode.
            ProviderCredentials.Apply(psi, provider.Key, claudeBilling: isClaude ? claudeBilling ?? ClaudeBillingMode.OfLogin(claudeConfigDir) : null);
            if (isClaude) ClaudeLogin.Apply(psi, claudeConfigDir);
            return psi;
        }

        // Ensure any project companion services (e.g. Prose.Hub) are running
        // across all providers (Claude, Codex, Gemini, Kimi).
        ProjectCompanionService.EnsureStarted(project, workingDir);

        Process? p = null;
        try
        {
            p = Process.Start(BuildStartInfo(resumeSession))
                ?? throw new InvalidOperationException($"Failed to start {parts[0]}");
            while (true)
            {
                if (p.WaitForExit(1000))
                {
                    p.WaitForExit();
                    return p.ExitCode;
                }

                RestartResumeRequest? request;
                lock (pendingLock) request = pending;
                var action = NextAction(request, p.Id);
                if (action is WatchAction.Wait) continue;

                lock (pendingLock) if (ReferenceEquals(pending, request)) pending = null;
                if (action is WatchAction.Drop) continue;

                if (action is WatchAction.Relocate)
                {
                    // Dock/Undock (M.LNCH-T78): the Orchestrator is moving this tab elsewhere, not asking for a
                    // restart here. End the CLI once idle and let Execute() return normally, so the standalone
                    // wt tab's window closes (or the embedded pane shows "ended") and the Orchestrator reopens
                    // it resumed wherever it's going.
                    System.Console.WriteLine();
                    System.Console.WriteLine("This session is relocating; ending here now that it's idle...");
                    try { p.Kill(entireProcessTree: true); }
                    catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or AggregateException) { }
                    p.WaitForExit();
                    return 0;
                }

                System.Console.WriteLine();
                claudeConfigDir = ClaudeLogin.Next(claudeConfigDir, request!);
                claudeBilling = ClaudeBillingMode.Next(claudeBilling, request!);
                System.Console.WriteLine(request!.Failover
                    ? request.Billing == ClaudeBillingMode.Api
                        ? $"No login has room: restarting Claude on the API key under {ClaudeLogin.Describe(claudeConfigDir)}, resuming this conversation..."
                        : $"Restarting Claude back on {ClaudeLogin.Describe(claudeConfigDir)} off the API key, resuming this conversation..."
                    : request.ConfigDir is not null
                    ? $"Restarting Claude under {ClaudeLogin.Describe(claudeConfigDir)}, resuming this conversation..."
                    : request.Billing == ClaudeBillingMode.Api
                        ? "Restarting Claude to bill the API key, resuming this conversation..."
                        : "Restarting Claude to bill your Claude Team login, resuming this conversation...");
                try { p.Kill(entireProcessTree: true); }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or AggregateException) { }
                p.WaitForExit();
                p.Dispose();
                p = Process.Start(BuildStartInfo(request.Session))
                    ?? throw new InvalidOperationException($"Failed to restart {parts[0]}");
            }
        }
        catch (Exception ex)
        {
            System.Console.Error.WriteLine($"Failed to launch {provider.Key}: {ex.Message}");
            return 3;
        }
        finally
        {
            p?.Dispose();
        }
    }

    /// <summary>
    /// Every agent tab starts on the newest code: <c>git pull --ff-only</c> in the
    /// tab's directory before the agent launches (this replaced the menu's Pull
    /// item). A failed pull — local changes in the way, diverged history, offline —
    /// is shown in the tab and the agent still starts on the local code; it never
    /// blocks the launch. Non-repos and repos with no remote are skipped quietly.
    /// </summary>
    private static void PullBeforeLaunch(string workingDir, string title) =>
        PullBeforeLaunch(workingDir, title, new GitService().Pull, System.Console.WriteLine, WriteWarning);

    private static void WriteWarning(string line)
    {
        var prior = System.Console.ForegroundColor;
        System.Console.ForegroundColor = ConsoleColor.Yellow;
        System.Console.WriteLine(line);
        System.Console.ForegroundColor = prior;
    }

    public static void PullBeforeLaunch(
        string workingDir, string title,
        Func<string, (bool ok, string? message)> pull,
        Action<string> write, Action<string> warn)
    {
        var dotGit = System.IO.Path.Combine(workingDir, ".git");
        if (!Directory.Exists(dotGit) && !File.Exists(dotGit)) return;

        write($"Pulling the latest {title}...");
        var (ok, message) = pull(workingDir);
        if (ok)
        {
            write($"  {message}.");
            return;
        }
        if (GitService.IsNoUpstream(message)) return;

        warn("  Pull failed; starting on the local code:");
        foreach (var line in (message ?? "").Split('\n'))
        {
            if (!string.IsNullOrWhiteSpace(line))
                warn($"    {line.TrimEnd()}");
        }
    }

    public enum WatchAction { Wait, Drop, Relaunch, Relocate }

    /// <summary>What the watch loop does this tick for the <paramref name="request"/> pending on the control pipe,
    /// if any: <c>Wait</c> (none pending, or pending but not yet idle), <c>Drop</c> (not for this CLI, or its
    /// billing mode moved on before its idle point — <see cref="ClaudeRestartGate"/>), <c>Relaunch</c> (an ordinary
    /// restart-resume, once idle), or <c>Relocate</c> (<c>"reason":"relocate"</c>, M.LNCH-T78, Dock/Undock) — the
    /// Orchestrator is moving this tab elsewhere, so the loop ends the CLI here instead of restarting it.</summary>
    public static WatchAction NextAction(RestartResumeRequest? request, int runningCliPid,
        string? billingModePath = null, string? sessionContextDirectory = null, DateTime? nowUtc = null)
    {
        if (request is null) return WatchAction.Wait;
        if (!ClaudeRestartGate.IsFor(request, runningCliPid) || !ClaudeRestartGate.StillWanted(request, billingModePath))
            return WatchAction.Drop;
        if (!ClaudeRestartGate.IsReady(request, sessionContextDirectory, nowUtc)) return WatchAction.Wait;
        return request.Relocate ? WatchAction.Relocate : WatchAction.Relaunch;
    }

    public readonly record struct RootResult(string? WorkingDir, Project? Project, string? Error, int ExitCode);

    /// <summary>--path wins over --name; an unknown name is 1, neither is 64.</summary>
    public static RootResult ResolveRoot(string? path, string? name, Func<string, Project?> findByName)
    {
        if (!string.IsNullOrWhiteSpace(path)) return new(path, null, null, 0);
        if (string.IsNullOrWhiteSpace(name)) return new(null, null, "--name or --path is required.", 64);
        var project = findByName(name);
        return project is null
            ? new(null, null, $"Unknown project: {name}", 1)
            : new(project.Path, project, null, 0);
    }

    public readonly record struct CommandResult(string[]? Parts, string? Error, int ExitCode);

    /// <summary>Splits the provider's RunCommand into argv; an empty one is exit 2.</summary>
    public static CommandResult ResolveCommand(AgentProvider provider)
    {
        var parts = CommandLineParser.Split(provider.RunCommand);
        return parts.Length == 0
            ? new(null, $"Provider {provider.Key} has an empty RunCommand.", 2)
            : new(parts, null, 0);
    }

    public readonly record struct ProviderResult(AgentProvider? Provider, string? Error, int ExitCode);

    /// <summary>An unknown explicit --provider is 4, never a fallback; none given is the first-listed provider.</summary>
    public static ProviderResult ResolveProvider(string? requestedKey, AgentProviderRegistry registry)
    {
        if (string.IsNullOrWhiteSpace(requestedKey)) return new(registry.Current(), null, 0);
        var requested = registry.ByKey(requestedKey);
        return requested is null
            ? new(null, $"Unknown provider: {requestedKey}", 4)
            : new(requested, null, 0);
    }

    /// <summary>--title, else --name, else the folder name of the path (trailing separator trimmed).</summary>
    public static string ResolveTitle(string? title, string? name, string workingDir) =>
        !string.IsNullOrWhiteSpace(title) ? title
        : !string.IsNullOrWhiteSpace(name) ? name
        : System.IO.Path.GetFileName(workingDir.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));

    /// <summary>The agent's start info before provider credentials and login: argv, working dir, NO_COLOR stripped, then --resume or the seed prompt last.</summary>
    public static ProcessStartInfo BuildBaseStartInfo(string[] parts, string workingDir, string? resumeSession, string? prompt)
    {
        // Bare npm-shim names (codex, gemini, ...) have no .exe on PATH, and
        // CreateProcessW doesn't walk PATHEXT, so resolve to the real shim first.
        if (parts.Length == 0) throw new ArgumentException("parts must hold the executable.", nameof(parts));
        var psi = new ProcessStartInfo(ExecutableResolver.Resolve(parts[0]))
        {
            UseShellExecute = false,
            WorkingDirectory = Directory.Exists(workingDir) ? workingDir : Environment.CurrentDirectory
        };
        for (var i = 1; i < parts.Length; i++) psi.ArgumentList.Add(parts[i]);
        // wt.exe can carry a stale NO_COLOR=1 that would strip every hosted agent's colors.
        psi.EnvironmentVariables.Remove("NO_COLOR");
        if (resumeSession is not null)
        {
            psi.ArgumentList.Add("--resume");
            psi.ArgumentList.Add(resumeSession);
        }
        else if (!string.IsNullOrWhiteSpace(prompt))
            psi.ArgumentList.Add(prompt);
        return psi;
    }
}
