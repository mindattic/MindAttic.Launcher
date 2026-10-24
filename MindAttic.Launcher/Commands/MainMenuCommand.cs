using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using MindAttic.Launcher.Menus;
using MindAttic.Launcher.Services;
using MindAttic.Launcher.Ui;
using Spectre.Console;
using Spectre.Console.Cli;

namespace MindAttic.Launcher.Commands;

[SuppressMessage("Performance", "CA1812", Justification = "Instantiated by Spectre.Console.Cli")]
public sealed class MainMenuCommand : AsyncCommand<MainMenuCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("--account <EMAIL>")]
        [Description("Run the whole Launcher as this Claude login: every Claude tab it opens runs under that account's own folder (signed in first when it isn't yet), and its window and tab titles show the login's alias.")]
        public string? Account { get; init; }
    }

    public override Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        // A Launcher scoped to one person (M.LNCH-T54): resolved, and signed in when it isn't yet, before
        // anything else opens, so no tab ever starts on a login nobody is in. Unscoped, nothing changes.
        // Without --account, ask once here who's using it (M.LNCH-T71) — from the org's current, eligible
        // members (WhoIsUsing.Resolve, backed by the orchestrator's own store) rather than per Claude tab
        // (the screen this replaced, M.LNCH-T65): no real choice (no roster, or one active member) asks
        // nothing, and every tab this session opens goes straight to the chosen person, no further question.
        var account = !string.IsNullOrWhiteSpace(settings.Account) ? settings.Account : WhoIsUsing.Resolve();
        AccountScope? scope = null;
        if (!string.IsNullOrWhiteSpace(account))
        {
            var (resolved, error) = AccountScope.Ensure(account);
            if (resolved is null)
            {
                System.Console.Error.WriteLine(error);
                return Task.FromResult(1);
            }
            scope = resolved;
            Screen.Scope = scope.Alias;
        }
        if (scope is not null)
        {
            try { System.Console.Title = scope.Stamp("MindAttic.Launcher"); } catch { /* no console */ }
        }

        // The console starts on the OEM code page, where the menu's rounded box
        // corners, scrollbar thumb (┃) and ellipsis (…) print as '?'. Switch the
        // menu to UTF-8 (not `host`: agent tabs keep whatever the agent expects).
        // Spectre's console already exists by now, writing through the OLD
        // encoder: left as-is it would send code-page bytes to a UTF-8 console and
        // every non-ASCII character would show as a replacement glyph. Recreate it
        // so it writes UTF-8 and detects Unicode support afresh.
        try
        {
            System.Console.OutputEncoding = System.Text.Encoding.UTF8;
            AnsiConsole.Console = AnsiConsole.Create(new AnsiConsoleSettings());
            AnsiConsole.Profile.Capabilities.Unicode = true;
        }
        catch (IOException) { /* redirected output: no console code page to set */ }

        var store = new SettingsStore();
        _ = store.Load(); // surface legacy-seed migration on launch

        var providers = new AgentProviderRegistry(store);
        var wt = new WindowsTerminalLauncher(scope);
        var git = new GitService();

        // A project registered before the topic filter existed has a null
        // RepoUrl, so GitHubRepoRef.Parse can't verify it against GitHub's
        // tagged set — it would silently vanish from every menu below even
        // when it IS tagged. Backfill from each repo's local origin remote
        // once, so the filter has something to match against.
        RepoUrlBackfill.Run(store, git.RemoteUrl);

        // Rescanned every launch (not cached in settings): tagging/untagging
        // the wip topic on GitHub is the lever for "what's in the
        // roster right now" — a repo surfaces the next time this starts, and
        // falls off just as quietly. Null (no token configured yet, or the
        // fetch failed) means "don't filter" — the roster degrades to showing
        // everything rather than going blank.
        var tagged = TopicRepoSync.FetchOrNull();

        var commit   = new CommitMenu(store, git, tagged);
        var open     = new OpenProjectMenu(store, providers, wt, tagged);
        var backup   = new BackupMenu(new BackupService(), store, new SqlBackupService(), new SqliteBackupService(), new AppDataBackupService(), tagged);
        var scheduledBackup = new ScheduledBackupMenu();
        var settingsMenu = new SettingsMenu(providers, store);
        var tickets = new TicketsMenu(Directory.Exists(ExePath.WorkspaceRoot) ? ExePath.WorkspaceRoot : MindAtticRoot());

        // Checked once at launch: running git every menu redraw would be wasteful,
        // and the build can't change underneath a running process anyway.
        var staleness = BuildFreshness.Check();
        // Token-spend line hidden for now: it gave no actionable insight. Restore
        // these two lines (here and the Screen.Add below) to bring it back.
        // var status = new ClaudeStatusService();

        // Offer any git repos found under the workspace that aren't in the roster
        // yet, so a freshly-created repo is added (with its color scheme) instead
        // of staying invisible to every menu until someone edits settings by hand.
        // Topic-filtered too: an untagged new repo shouldn't interrupt startup
        // with an add-to-roster prompt until it's actually tagged.
        new DiscoverProjectsMenu(store, git, new WindowsTerminalSchemes(), tagged).Run();

        var items = new List<MenuItem>
        {
            // First row = the highlighted default on launch (resumeIndex starts at 0).
            new() { Name = "Open Project Tab",              Description = "select a project, then pick which agent CLI to open it with", Tag = "open" },
            new() { Name = "Commit and sync",               Description = "commit and push changes per project or across all", Tag = "commit" },
            new() { Name = "Tickets",                       Description = "open tickets, repo status and ticket-log checks across the workspace", Tag = "tickets" },
            new() { Name = "Backup",                        Description = "back up MindAttic to R:\\Backup\\MindAttic", Tag = "backup" },
            new() { Name = "Scheduled Backup",              Description = "status and toggle for the nightly backup task", Tag = "schedule" },
            new() { Name = "Orchestrator",                  Description = "the dashboard: every session, the token budget and pace, the ticket queue", Tag = "orchestrator" },
            new() { Name = "Settings",                      Description = "CLI development: the model each agent CLI runs with", Tag = "settings" },
            new() { Name = "Open Command Prompt (Admin)",   Description = "open cmd as Administrator at the workspace root", Tag = "cmd" },
            new() { Name = "Open PowerShell (Admin)",       Description = "open PowerShell as Administrator at the workspace root", Tag = "ps" },
            new() { Name = "Restart",                       Description = "reload this console in a new tab; other tabs are untouched", Tag = "restart" },
            new() { Name = "Quit",                          Description = "close this menu (other tabs are untouched)", Tag = "exit", SpacerBefore = true }
        };

        var resumeIndex = 0;
        while (true)
        {
            Screen.Header();
            // Via Screen.Add so a window resize repaints them above the menu.
            Screen.Add(() => RenderStaleness(staleness));
            // Screen.Add(status.Render);

            // Cancel after 60 s so the menu redraw loop wakes up and reprints
            // fresh status even when the user is idle.
            using var refreshCts = new CancellationTokenSource(TimeSpan.FromMinutes(1));
            var result = Ui.Menu.PromptWithKeys(
                "MindAttic Launcher — choose an action:",
                items,
                customKeys: null,
                allowBack: false,
                refreshToken: refreshCts.Token,
                initialIndex: resumeIndex);

            resumeIndex = result.Index;
            if (result.Timeout) continue;
            if (result.Selected is null) return Task.FromResult(0);

            switch (result.Selected.Tag)
            {
                case "commit":   commit.Run(); break;
                case "tickets": tickets.Run(); break;
                case "open":     open.Run(); break;
                case "backup":   backup.Run(); break;
                case "schedule": scheduledBackup.Run(); break;
                case "orchestrator": OpenOrchestrator(); break;
                case "settings": settingsMenu.Run(); break;
                case "cmd":
                case "ps":
                {
                    // Open at the MindAttic workspace root (the parent that holds
                    // every repo), matching "the root directory" in the hint —
                    // MindAtticRoot() is the Launcher *repo* (Deploy needs it that
                    // way), which is a subfolder, not the workspace.
                    var adminRoot = ExePath.WorkspaceRoot;
                    if (!Directory.Exists(adminRoot)) adminRoot = MindAtticRoot();
                    var tab = result.Selected.Tag is "ps"
                        ? wt.BuildPowerShellTab(adminRoot)
                        : wt.BuildCmdTab(adminRoot);
                    Screen.Working("Opening elevated tab…  Please wait.");
                    wt.OpenElevated(tab);
                    Thread.Sleep(600);
                    break;
                }
                case "restart":
                    Screen.Working("Rebuilding and restarting…  Please wait.");
                    RestartInNewTab(wt, scope);
                    Thread.Sleep(1000);
                    return Task.FromResult(0);
                case "exit":     return Task.FromResult(0);
            }
        }
    }

    private static void RenderStaleness(BuildFreshness.Result? r)
    {
        if (r is null) return;

        // The menu process can't rebuild itself; Restart republishes and reloads.
        // DaysBehind floors to whole days, so 0 means "behind by less than a day"
        // — not "built today" (the build itself may be days old; it's the *gap*
        // to HEAD that's under a day).
        var age = r.DaysBehind >= 1
            ? $"[yellow]{r.DaysBehind} day{(r.DaysBehind == 1 ? "" : "s")}[/] behind the latest commit"
            : "[yellow]less than a day[/] behind the latest commit";
        AnsiConsole.MarkupLine($"  [grey50]Heads up:[/] this menu build is {age}. [grey50]Choose Restart to republish & reload.[/]");
        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Opens the orchestrator dashboard where it installs itself (C:\Apps, or per-user Programs when
    /// C:\Apps isn't writable). It's a separate app, so it opens beside this menu, not in a tab.
    /// </summary>
    private static void OpenOrchestrator()
    {
        var exe = new[]
        {
            @"C:\Apps\MindAttic.Orchestrator\MindAttic.Orchestrator.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "MindAttic.Orchestrator", "MindAttic.Orchestrator.exe"),
        }.FirstOrDefault(File.Exists);
        if (exe is null)
        {
            AnsiConsole.MarkupLine("[yellow]The orchestrator isn't installed. Double-click MindAttic.Orchestrator.exe once (or run its deploy.ps1) and it installs itself.[/]");
            Thread.Sleep(2500);
            return;
        }
        Screen.Working("Opening the orchestrator…");
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe) });
        Thread.Sleep(800);
    }

    private static string MindAtticRoot()
    {
        var legacy = SettingsStore.DefaultLegacySettingsPath;
        var root = Path.GetDirectoryName(legacy);
        return Directory.Exists(root) ? root! : Environment.CurrentDirectory;
    }

    private static void RestartInNewTab(WindowsTerminalLauncher wt, AccountScope? scope)
    {
        // We don't republish in-process: this menu usually runs from the deployed exe
        // in C:\Apps\MindAttic.Launcher, which Windows locks while it runs. Instead
        // run deploy.ps1 -WaitPid in a fresh tab; it waits for THIS process to exit,
        // force-rebuilds, then runs the fresh exe (and refuses to launch a stale one
        // if the build fails). We return 0 right after, so this tab closes.
        // A scoped Launcher (M.LNCH-T54) restarts scoped to the same login, in that person's window (M.LNCH-T55).
        var root = ExePath.RepoRoot;
        var restartScript = root is null ? null : Path.Combine(root, "deploy.ps1");
        var title = scope?.Stamp("MindAttic.Launcher") ?? "MindAttic.Launcher";
        string[] launchArgs = scope is null ? [] : ["--account", scope.Email];

        if (restartScript is null || !File.Exists(restartScript))
        {
            // No repo/script to publish from (e.g. an installed copy) — just relaunch
            // whatever exe currently exists; there's nothing to rebuild against.
            wt.Open(new WindowsTerminalLauncher.Tab
            {
                Window = wt.ScopeWindow,
                Title = title,
                WorkingDirectory = MindAtticRoot(),
                Command = [ExePath.Release, .. launchArgs]
            });
            return;
        }

        wt.Open(new WindowsTerminalLauncher.Tab
        {
            Window = wt.ScopeWindow,
            Title = title,
            WorkingDirectory = MindAtticRoot(),
            Command =
            [
                "powershell", "-NoProfile", "-ExecutionPolicy", "Bypass",
                "-File", restartScript,
                "-WaitPid", Environment.ProcessId.ToString(),
                .. launchArgs
            ]
        });
    }
}
