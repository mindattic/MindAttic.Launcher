using System.Diagnostics.CodeAnalysis;
using MindAttic.Console.Menus;
using MindAttic.Console.Services;
using MindAttic.Console.Ui;
using Spectre.Console;
using Spectre.Console.Cli;

namespace MindAttic.Console.Commands;

[SuppressMessage("Performance", "CA1812", Justification = "Instantiated by Spectre.Console.Cli")]
public sealed class MainMenuCommand : AsyncCommand<MainMenuCommand.Settings>
{
    public sealed class Settings : CommandSettings { }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var store = new SettingsStore();
        _ = store.Load(); // surface legacy-seed migration on launch

        var providers = new AgentProviderRegistry(store);
        var wt = new WindowsTerminalLauncher();
        var git = new GitService();

        var commit   = new CommitMenu(store, git);
        var pull     = new PullMenu(store, git);
        var open     = new OpenProjectMenu(store, providers, wt);
        var run      = new RunProjectMenu(store, wt);
        var backup   = new BackupMenu(new BackupService());
        var provider = new ProviderMenu(store, providers);

        while (true)
        {
            Screen.Header();

            var items = new List<MenuItem>
            {
                new() { Name = "Commit and sync",     Description = "commit and push changes per project or across all", Tag = "commit" },
                new() { Name = "Pull",                Description = "git pull --ff-only per project or across all", Tag = "pull" },
                new() { Name = "Open Project Tab",    Description = "select a project to open with its configured coding agent", Tag = "open" },
                new() { Name = "Run Project",         Description = "run a project in a new terminal tab", Tag = "run" },
                new() { Name = "Backup",              Description = "back up MindAttic to R:\\Backup\\MindAttic", Tag = "backup" },
                new() { Name = "Provider",            Description = "set default coding agent or per-project override", Tag = "provider" },
                new() { Name = "Remote Control",      Description = "run /remote-control in every open Claude tab", Tag = "remote" },
                new() { Name = "Open Command Prompt", Description = "open cmd at the root directory", Tag = "cmd" },
                new() { Name = "Restart",             Description = "reload this console in a new tab; other tabs are untouched", Tag = "restart" },
                new() { Name = "Exit",                Description = "close this menu (other tabs are untouched)", Tag = "exit" }
            };

            var sel = Ui.Menu.Prompt("MindAttic Console — choose an action:", items, allowBack: false);
            if (sel is null) return 0;

            switch (sel.Tag)
            {
                case "commit":   commit.Run(); break;
                case "pull":     pull.Run(); break;
                case "open":     open.Run(); break;
                case "run":      run.Run(); break;
                case "backup":   backup.Run(); break;
                case "provider": provider.Run(); break;
                case "remote":   await RunRemoteControl(); break;
                case "cmd":
                    wt.Open(wt.BuildCmdTab(MindAtticRoot()));
                    Thread.Sleep(600);
                    break;
                case "restart":
                    RestartInNewTab(wt);
                    return 0;
                case "exit":     return 0;
            }
        }
    }

    private static async Task RunRemoteControl()
    {
        var broadcaster = new RemoteControlBroadcaster();
        var result = await broadcaster.BroadcastAsync("Claude", "/remote-control\n");

        if (result.Delivered == 0 && result.Failed.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No open Claude tabs found.[/]");
        }
        else
        {
            var tabWord = result.Delivered == 1 ? "tab" : "tabs";
            AnsiConsole.MarkupLine($"[green]Sent /remote-control to {result.Delivered} Claude {tabWord}.[/]");
            if (result.Failed.Count > 0)
                AnsiConsole.MarkupLine($"[red]{result.Failed.Count} tab(s) didn't respond.[/]");
        }
        Thread.Sleep(1200);
    }

    private static string MindAtticRoot()
    {
        var legacy = SettingsStore.DefaultLegacySettingsPath;
        var root = Path.GetDirectoryName(legacy);
        return Directory.Exists(root) ? root! : Environment.CurrentDirectory;
    }

    private static void RestartInNewTab(WindowsTerminalLauncher wt)
    {
        // Republish if source has changed so the new tab runs current code,
        // and target the canonical Release exe in artifacts/ rather than
        // ExePath.Self (which could be a Debug bin path during dev).
        ExePath.EnsureFresh();
        wt.Open(new WindowsTerminalLauncher.Tab
        {
            Title = "MindAttic.Console",
            WorkingDirectory = MindAtticRoot(),
            Command = [ExePath.Release]
        });
        Thread.Sleep(600);
    }
}
