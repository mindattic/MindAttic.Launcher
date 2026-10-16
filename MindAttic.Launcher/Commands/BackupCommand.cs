using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using MindAttic.Launcher.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace MindAttic.Launcher.Commands;

/// <summary>
/// Out-of-menu, non-interactive backup subcommand — what a scheduled task calls instead of the
/// interactive Backup menu. Runs the exact same four services
/// (<see cref="BackupService"/>/<see cref="AppDataBackupService"/>/<see cref="SqlBackupService"/>/
/// <see cref="SqliteBackupService"/>) <c>Menus.BackupMenu</c> uses, so "manual" and "automated"
/// backups are provably the same code path, not two implementations that can drift apart.
/// </summary>
[SuppressMessage("Performance", "CA1812", Justification = "Instantiated by Spectre.Console.Cli")]
public sealed class BackupCommand : Command<BackupCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("--keep <N>")]
        [Description("Prune dated backup folders beyond the newest N (default: keep every one).")]
        public int? Keep { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var store = new SettingsStore();
        var backup = new BackupService();
        var appData = new AppDataBackupService();
        var sql = new SqlBackupService();
        var sqlite = new SqliteBackupService();

        string target;
        try
        {
            target = backup.ResolveTargetFolder();
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(ex.Message)}[/]");
            return 1;
        }
        AnsiConsole.MarkupLine($"[white]Backup target:[/] {Markup.Escape(target)}");

        var ok = true;

        var fileResult = backup.Run(target);
        ok &= fileResult.Ok;
        AnsiConsole.MarkupLine(fileResult.Ok
            ? $"[white]Files:[/] [green]OK[/] ({FormatBytes(fileResult.BytesCopied)})"
            : $"[white]Files:[/] [red]FAILED (robocopy exit {fileResult.RobocopyExitCode})[/]");

        var appDataResult = appData.Zip(target);
        ok &= appDataResult.Ok;
        AnsiConsole.MarkupLine(appDataResult.Ok
            ? $"[white]AppData:[/] [green]OK[/] ({FormatBytes(appDataResult.Bytes)})"
            : $"[white]AppData:[/] [red]FAILED[/] {Markup.Escape(appDataResult.Output)}");

        var settingsData = store.Load();
        var dbTargets = SqlBackupService.CollectTargets(settingsData);
        var localDb = sql.DiscoverDatabases(SqlBackupService.LocalDbInstance);
        if (localDb.Ok)
        {
            dbTargets = SqlBackupService.Merge(dbTargets, localDb.Targets);
            AnsiConsole.MarkupLine($"[white]LocalDB discovery:[/] [green]OK[/] ({localDb.Targets.Count} found)");
        }
        else
        {
            AnsiConsole.MarkupLine($"[yellow]LocalDB discovery skipped:[/] {Markup.Escape(localDb.Output)}");
        }
        var dbResults = sql.Backup(dbTargets, target);
        foreach (var r in dbResults)
        {
            ok &= r.Ok;
            AnsiConsole.MarkupLine(r.Ok
                ? $"[white]{Markup.Escape(r.Database)}@{Markup.Escape(r.Server)}:[/] [green]OK[/]"
                : $"[white]{Markup.Escape(r.Database)}@{Markup.Escape(r.Server)}:[/] [red]FAILED (exit {r.ExitCode})[/]");
        }

        var sqliteTargets = SqliteBackupService.CollectTargets(settingsData);
        var sqliteResults = sqlite.Backup(sqliteTargets, target);
        foreach (var r in sqliteResults)
        {
            ok &= r.Ok;
            AnsiConsole.MarkupLine(r.Ok
                ? $"[white]{Markup.Escape(r.Project)} ({Markup.Escape(r.DatabaseFile)}):[/] [green]OK[/]"
                : $"[white]{Markup.Escape(r.Project)} ({Markup.Escape(r.DatabaseFile)}):[/] [red]FAILED (exit {r.ExitCode})[/]");
        }

        var pruned = 0;
        if (settings.Keep is { } keep)
        {
            var existing = Directory.Exists(backup.BackupBase)
                ? Directory.GetDirectories(backup.BackupBase)
                : [];
            var stale = BackupService.ResolveStaleFolders(existing, keep);
            foreach (var folder in stale)
            {
                try
                {
                    Directory.Delete(folder, recursive: true);
                    pruned++;
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[yellow]Could not prune {Markup.Escape(folder)}: {Markup.Escape(ex.Message)}[/]");
                }
            }
            if (pruned > 0) AnsiConsole.MarkupLine($"[white]Pruned:[/] {pruned} old backup folder(s)");
        }

        Serilog.Log.Information(
            "MindAttic.Launcher backup {Outcome}: target={Target} files={FilesOk} appData={AppDataOk} databases={DbOk}/{DbTotal} sqlite={SqliteOk}/{SqliteTotal} pruned={Pruned}",
            ok ? "OK" : "PARTIAL FAILURE", target, fileResult.Ok, appDataResult.Ok,
            dbResults.Count(r => r.Ok), dbResults.Count,
            sqliteResults.Count(r => r.Ok), sqliteResults.Count, pruned);

        return ok ? 0 : 1;
    }

    private static string FormatBytes(long bytes) =>
        bytes switch
        {
            < 1024L => $"{bytes} B",
            < 1024L * 1024 => $"{bytes / 1024.0:F2} KB",
            < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F2} MB",
            _ => $"{bytes / (1024.0 * 1024 * 1024):F2} GB"
        };
}
