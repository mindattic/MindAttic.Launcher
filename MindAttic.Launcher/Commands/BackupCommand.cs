using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using MindAttic.Launcher.Models;
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

    /// <summary>The services <see cref="Run"/> drives, as delegates so tests never reach robocopy/sqlcmd/sqlite3.</summary>
    public sealed record Deps(
        string BackupBase,
        Func<string> ResolveTargetFolder,
        Func<string, BackupResult> RunFiles,
        Func<string, AppDataBackupResult> ZipAppData,
        Func<AppSettings> LoadSettings,
        Func<string, DatabaseDiscoveryResult> DiscoverLocalDb,
        Func<IReadOnlyList<BackupTarget>, string, IReadOnlyList<DatabaseBackupResult>> BackupSql,
        Func<IReadOnlyList<SqliteBackupTarget>, string, IReadOnlyList<SqliteBackupResult>> BackupSqlite,
        Func<string, IReadOnlyList<string>> ListFolders,
        Action<string> DeleteFolder);

    public static Deps Live()
    {
        var store = new SettingsStore();
        var backup = new BackupService();
        var appData = new AppDataBackupService();
        var sql = new SqlBackupService();
        var sqlite = new SqliteBackupService();
        return new Deps(
            backup.BackupBase,
            backup.ResolveTargetFolder,
            t => backup.Run(t),
            appData.Zip,
            store.Load,
            server => sql.DiscoverDatabases(server),
            (targets, t) => sql.Backup(targets, t),
            (targets, t) => sqlite.Backup(targets, t),
            baseFolder => Directory.Exists(baseFolder) ? Directory.GetDirectories(baseFolder) : [],
            folder => Directory.Delete(folder, recursive: true));
    }

    public override int Execute(CommandContext context, Settings settings) => Run(settings.Keep, Live());

    public static int Run(int? keepOption, Deps d)
    {
        string target;
        try
        {
            target = d.ResolveTargetFolder();
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(ex.Message)}[/]");
            return 1;
        }
        AnsiConsole.MarkupLine($"[white]Backup target:[/] {Markup.Escape(target)}");

        var ok = true;

        var fileResult = d.RunFiles(target);
        ok &= fileResult.Ok;
        AnsiConsole.MarkupLine(fileResult.Ok
            ? $"[white]Files:[/] [green]OK[/] ({FormatBytes(fileResult.BytesCopied)})"
            : $"[white]Files:[/] [red]FAILED (robocopy exit {fileResult.RobocopyExitCode})[/]");

        var appDataResult = d.ZipAppData(target);
        ok &= appDataResult.Ok;
        AnsiConsole.MarkupLine(appDataResult.Ok
            ? $"[white]AppData:[/] [green]OK[/] ({FormatBytes(appDataResult.Bytes)})"
            : $"[white]AppData:[/] [red]FAILED[/] {Markup.Escape(appDataResult.Output)}");

        var settingsData = d.LoadSettings();
        var dbTargets = SqlBackupService.CollectTargets(settingsData);
        var localDb = d.DiscoverLocalDb(SqlBackupService.LocalDbInstance);
        if (localDb.Ok)
        {
            dbTargets = SqlBackupService.Merge(dbTargets, localDb.Targets);
            AnsiConsole.MarkupLine($"[white]LocalDB discovery:[/] [green]OK[/] ({localDb.Targets.Count} found)");
        }
        else
        {
            AnsiConsole.MarkupLine($"[yellow]LocalDB discovery skipped:[/] {Markup.Escape(localDb.Output)}");
        }
        var dbResults = d.BackupSql(dbTargets, target);
        foreach (var r in dbResults)
        {
            ok &= r.Ok;
            AnsiConsole.MarkupLine(r.Ok
                ? $"[white]{Markup.Escape(r.Database)}@{Markup.Escape(r.Server)}:[/] [green]OK[/]"
                : $"[white]{Markup.Escape(r.Database)}@{Markup.Escape(r.Server)}:[/] [red]FAILED (exit {r.ExitCode})[/]");
        }

        var sqliteTargets = SqliteBackupService.CollectTargets(settingsData);
        var sqliteResults = d.BackupSqlite(sqliteTargets, target);
        foreach (var r in sqliteResults)
        {
            ok &= r.Ok;
            AnsiConsole.MarkupLine(r.Ok
                ? $"[white]{Markup.Escape(r.Project)} ({Markup.Escape(r.DatabaseFile)}):[/] [green]OK[/]"
                : $"[white]{Markup.Escape(r.Project)} ({Markup.Escape(r.DatabaseFile)}):[/] [red]FAILED (exit {r.ExitCode})[/]");
        }

        var pruned = 0;
        if (keepOption is not null && !ok)
        {
            // A failed run must never delete older backups: they may be the only good ones left.
            AnsiConsole.MarkupLine("[yellow]Backup had failures; skipping prune of older backup folders.[/]");
        }
        else if (keepOption is { } keep)
        {
            var existing = d.ListFolders(d.BackupBase);
            var stale = BackupService.ResolveStaleFolders(existing, keep);
            foreach (var folder in stale)
            {
                try
                {
                    d.DeleteFolder(folder);
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

    public static string FormatBytes(long bytes) =>
        bytes switch
        {
            < 1024L => $"{bytes} B",
            < 1024L * 1024 => $"{bytes / 1024.0:F2} KB",
            < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F2} MB",
            _ => $"{bytes / (1024.0 * 1024 * 1024):F2} GB"
        };
}
