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
    public sealed class Settings : CommandSettings { }

    public override Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        // The console starts on the OEM code page, where the menu's rounded box
        // corners, scrollbar thumb (┃) and ellipsis (…) print as '?'. Switch the
        // menu to UTF-8 (not `host`: agent tabs keep whatever the agent expects),
        // and tell Spectre, which sized up Unicode support when it was created.
        try
        {
            System.Console.OutputEncoding = System.Text.Encoding.UTF8;
            AnsiConsole.Profile.Capabilities.Unicode = true;
        }
        catch (IOException) { /* redirected output: no console code page to set */ }

        var store = new SettingsStore();
        _ = store.Load(); // surface legacy-seed migration on launch

        var providers = new AgentProviderRegistry(store);
        var wt = new WindowsTerminalLauncher();
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
        var pull     = new PullMenu(store, git, tagged);
        var open     = new OpenProjectMenu(store, providers, wt, tagged);
        var backup   = new BackupMenu(new BackupService(), store, new SqlBackupService(), tagged);
        var settingsMenu = new SettingsMenu(providers);
        var workflow = new WorkflowMenu(Directory.Exists(ExePath.WorkspaceRoot) ? ExePath.WorkspaceRoot : MindAtticRoot());

        // Checked once at launch: running git every menu redraw would be wasteful,
        // and the build can't change underneath a running process anyway.
        var staleness = BuildFreshness.Check();
        var status = new ClaudeStatusService();

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
            new() { Name = "Pull",                          Description = "git pull --ff-only per project or across all", Tag = "pull" },
            new() { Name = "Workflow",                      Description = "open tickets, repo status and ticket-log checks across the workspace", Tag = "workflow" },
            new() { Name = "Backup",                        Description = "back up MindAttic to R:\\Backup\\MindAttic", Tag = "backup" },
            new() { Name = "Settings",                      Description = "CLI development: the model each agent CLI runs with", Tag = "settings" },
            new() { Name = "Status",                        Description = "open a Claude tab at the workspace root with /status pre-filled", Tag = "status" },
            new() { Name = "Open Command Prompt (Admin)",   Description = "open cmd as Administrator at the workspace root", Tag = "cmd" },
            new() { Name = "Open PowerShell (Admin)",       Description = "open PowerShell as Administrator at the workspace root", Tag = "ps" },
            new() { Name = "Restart",                       Description = "reload this console in a new tab; other tabs are untouched", Tag = "restart" },
            new() { Name = "Exit",                          Description = "close this menu (other tabs are untouched)", Tag = "exit" }
        };

        var resumeIndex = 0;
        while (true)
        {
            Screen.Header();
            RenderStaleness(staleness);
            status.Render();

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
                case "pull":     pull.Run(); break;
                case "workflow": workflow.Run(); break;
                case "open":     open.Run(); break;
                case "backup":   backup.Run(); break;
                case "settings": settingsMenu.Run(); break;
                case "status":
                {
                    var statusRoot = ExePath.WorkspaceRoot;
                    if (!Directory.Exists(statusRoot)) statusRoot = MindAtticRoot();
                    var provider = providers.Current();
                    Screen.Working("Opening status tab…  Please wait.");
                    ExePath.EnsureFresh();
                    wt.Open(wt.BuildAgentTabAtPath(
                        $"Status [{provider.Key}]", statusRoot, provider, ExePath.Release,
                        prompt: "/status"));
                    Thread.Sleep(800);
                    break;
                }
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
                    RestartInNewTab(wt);
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

    private static string MindAtticRoot()
    {
        var legacy = SettingsStore.DefaultLegacySettingsPath;
        var root = Path.GetDirectoryName(legacy);
        return Directory.Exists(root) ? root! : Environment.CurrentDirectory;
    }

    private static void RestartInNewTab(WindowsTerminalLauncher wt)
    {
        // We don't republish in-process: this menu usually runs from the deployed exe
        // in C:\Apps\MindAttic.Launcher, which Windows locks while it runs. Instead
        // run deploy.ps1 -WaitPid in a fresh tab; it waits for THIS process to exit,
        // force-rebuilds, then runs the fresh exe (and refuses to launch a stale one
        // if the build fails). We return 0 right after, so this tab closes.
        var root = ExePath.RepoRoot;
        var restartScript = root is null ? null : Path.Combine(root, "deploy.ps1");

        if (restartScript is null || !File.Exists(restartScript))
        {
            // No repo/script to publish from (e.g. an installed copy) — just relaunch
            // whatever exe currently exists; there's nothing to rebuild against.
            wt.Open(new WindowsTerminalLauncher.Tab
            {
                Title = "MindAttic.Launcher",
                WorkingDirectory = MindAtticRoot(),
                Command = [ExePath.Release]
            });
            return;
        }

        wt.Open(new WindowsTerminalLauncher.Tab
        {
            Title = "MindAttic.Launcher",
            WorkingDirectory = MindAtticRoot(),
            Command =
            [
                "powershell", "-NoProfile", "-ExecutionPolicy", "Bypass",
                "-File", restartScript,
                "-WaitPid", Environment.ProcessId.ToString()
            ]
        });
    }
}
