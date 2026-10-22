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
        [Description("Run a Claude tab's CLI under this login folder (CLAUDE_CONFIG_DIR). A Launcher scoped to one account (--account) puts it in every Claude tab's command, since Windows Terminal tabs don't inherit the Launcher's environment.")]
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
        Project? project = null;
        string workingDir;
        if (!string.IsNullOrWhiteSpace(settings.Path))
        {
            workingDir = settings.Path!;
        }
        else if (!string.IsNullOrWhiteSpace(settings.Name))
        {
            project = ProjectRoster.FindByName(store.Load(), settings.Name);
            if (project is null)
            {
                System.Console.Error.WriteLine($"Unknown project: {settings.Name}");
                return 1;
            }
            workingDir = project.Path;
        }
        else
        {
            System.Console.Error.WriteLine("--name or --path is required.");
            return 64;
        }

        AgentProvider provider;
        if (!string.IsNullOrWhiteSpace(settings.Provider))
        {
            // An explicit --provider that doesn't resolve is a caller bug (a
            // typo), not "quietly launch the project default" — that would hide
            // the mistake and start the wrong agent. Fail loudly instead.
            var requested = registry.ByKey(settings.Provider);
            if (requested is null)
            {
                System.Console.Error.WriteLine($"Unknown provider: {settings.Provider}");
                return 4;
            }
            provider = requested;
        }
        else
        {
            // No explicit --provider: there's no per-project override anymore
            // (that's now an ephemeral, menu-time choice — see OpenProjectMenu),
            // so a direct/scripted `host` call always resolves to the
            // first-listed provider (Claude, per AgentProviderRegistry.Defaults).
            provider = registry.Current();
        }

        var title = !string.IsNullOrWhiteSpace(settings.Title) ? settings.Title!
                  : !string.IsNullOrWhiteSpace(settings.Name) ? settings.Name
                  : System.IO.Path.GetFileName(workingDir.TrimEnd(System.IO.Path.DirectorySeparatorChar));

        var parts = CommandLineParser.Split(provider.RunCommand);
        if (parts.Length == 0)
        {
            System.Console.Error.WriteLine($"Provider {provider.Key} has an empty RunCommand.");
            return 2;
        }

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
        // An explicit --config-dir (a Launcher scoped to one account, M.LNCH-T54) wins over whatever
        // was inherited, and being non-empty it also skips the question.
        var claudeConfigDir = ClaudeLogin.Initial(!string.IsNullOrWhiteSpace(settings.ConfigDir)
            ? settings.ConfigDir
            : Environment.GetEnvironmentVariable(ClaudeLogin.ConfigDirVariable));
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
            else if (string.IsNullOrWhiteSpace(claudeConfigDir)
                     && resumeSession is not null
                     && ClaudeResume.FindConfigDir(resumeSession, ClaudeResume.SignedInLogins()) is { } sessionLogin)
            {
                // A resumed conversation belongs to the login that wrote it; asking would only let the
                // tab pick one where `claude --resume` can't find it.
                claudeConfigDir = sessionLogin;
                showAccountInTitle = ClaudeAccountCatalog.PerCliLogins().Count > 0;
            }
            else if (string.IsNullOrWhiteSpace(claudeConfigDir))
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
            var psi = new ProcessStartInfo(ExecutableResolver.Resolve(parts[0]))
            {
                UseShellExecute = false,
                // Working directory matches the wt tab so the agent starts in the
                // right root; menu code already passes -d to wt, but be defensive in
                // case someone runs `mindattic host` directly.
                WorkingDirectory = Directory.Exists(workingDir) ? workingDir : Environment.CurrentDirectory
            };
            for (var i = 1; i < parts.Length; i++) psi.ArgumentList.Add(parts[i]);
            // wt.exe can carry a stale NO_COLOR=1 from a since-unset registry value
            // (it only re-reads the environment on its own fresh launch), which
            // would otherwise flow down and strip every hosted agent's colors.
            psi.EnvironmentVariables.Remove("NO_COLOR");
            if (resumeSession is not null)
            {
                // A restart carries the conversation on; the seed prompt was its first turn's.
                psi.ArgumentList.Add("--resume");
                psi.ArgumentList.Add(resumeSession);
            }
            // A seed prompt is the agent's first positional arg — `claude <flags>
            // "<order>"` / `codex <flags> "<order>"`. Both start interactive with
            // the prompt loaded; the Status tab uses this to pre-fill /status.
            else if (!string.IsNullOrWhiteSpace(settings.Prompt))
                psi.ArgumentList.Add(settings.Prompt!);

            // Push this provider's API key (if Vault has one) to wherever its CLI
            // expects it — an env var for Gemini, config.toml for Kimi. A missing
            // Vault entry is a no-op; the CLI falls back to its own configured auth.
            // Claude follows the Orchestrator's billing mode (ClaudeBillingMode).
            ProviderCredentials.Apply(psi, provider.Key);
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
                if (request is null) continue;

                // Only the CLI the request names, and only while the mode is still the one asked for;
                // anything else is dropped. One waiting for its idle point stays pending.
                if (!ClaudeRestartGate.IsFor(request, p.Id) || !ClaudeRestartGate.StillWanted(request))
                {
                    lock (pendingLock) if (ReferenceEquals(pending, request)) pending = null;
                    continue;
                }
                if (!ClaudeRestartGate.IsIdle(request)) continue;
                lock (pendingLock) if (ReferenceEquals(pending, request)) pending = null;

                System.Console.WriteLine();
                claudeConfigDir = ClaudeLogin.Next(claudeConfigDir, request);
                System.Console.WriteLine(request.ConfigDir is not null
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
    private static void PullBeforeLaunch(string workingDir, string title)
    {
        var dotGit = System.IO.Path.Combine(workingDir, ".git");
        if (!Directory.Exists(dotGit) && !File.Exists(dotGit)) return;

        System.Console.WriteLine($"Pulling the latest {title}...");
        var (ok, message) = new GitService().Pull(workingDir);
        if (ok)
        {
            System.Console.WriteLine($"  {message}.");
            return;
        }
        if (GitService.IsNoUpstream(message)) return;

        var prior = System.Console.ForegroundColor;
        System.Console.ForegroundColor = ConsoleColor.Yellow;
        System.Console.WriteLine("  Pull failed; starting on the local code:");
        foreach (var line in (message ?? "").Split('\n'))
        {
            if (!string.IsNullOrWhiteSpace(line))
                System.Console.WriteLine($"    {line.TrimEnd()}");
        }
        System.Console.ForegroundColor = prior;
    }
}
