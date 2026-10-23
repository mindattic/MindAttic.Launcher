using MindAttic.Launcher.Services;
using MindAttic.Launcher.Ui;
using Spectre.Console;

namespace MindAttic.Launcher.Menus;

public sealed class BackupMenu(
    BackupService backup, SettingsStore store, SqlBackupService sql,
    SqliteBackupService sqlite, AppDataBackupService appData, IReadOnlySet<string>? tagged = null)
{
    public BackupMenu() : this(new BackupService(), new SettingsStore(), new SqlBackupService(), new SqliteBackupService(), new AppDataBackupService()) { }

    public void Run()
    {
        Screen.Header("Backup");

        string target;
        try
        {
            target = backup.ResolveTargetFolder();
        }
        catch (Exception ex)
        {
            // ResolveTargetFolder throws when all 27 dated slots for today are
            // taken — surface it as a notice instead of an unhandled crash.
            Screen.Notice($"[red]{Markup.Escape(ex.Message)}[/]");
            Screen.PressAnyKey();
            return;
        }

        // A settings read failure must not block the file backup — degrade to
        // "no databases" and warn, rather than aborting the whole backup because
        // the roster couldn't be loaded.
        IReadOnlyList<BackupTarget> dbTargets;
        IReadOnlyList<SqliteBackupTarget> sqliteTargets;
        try
        {
            var settings = store.Load();
            dbTargets = SqlBackupService.CollectTargets(settings, tagged);
            sqliteTargets = SqliteBackupService.CollectTargets(settings, tagged);

            // Round out the roster with whatever's actually on LocalDB (M.LNCH-T21):
            // EF/VS-created databases there are rarely added to a project's
            // roster entry, so roster-only would silently skip them. Best-effort
            // — a machine with no LocalDB just contributes nothing here.
            var localDb = sql.DiscoverDatabases(SqlBackupService.LocalDbInstance);
            if (localDb.Ok && localDb.Targets.Count > 0)
                dbTargets = SqlBackupService.Merge(dbTargets, localDb.Targets);
        }
        catch (Exception ex)
        {
            dbTargets = [];
            sqliteTargets = [];
            AnsiConsole.MarkupLine($"  [yellow]Could not read project databases: {Markup.Escape(ex.Message)}[/]");
            AnsiConsole.MarkupLine("  [grey50]Continuing with the file backup only.[/]");
        }

        AnsiConsole.MarkupLine("  You are about to back up:");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"    [yellow]From:[/] {Markup.Escape(backup.Source)}");
        AnsiConsole.MarkupLine($"    [yellow]To:  [/] {Markup.Escape(target)}");
        if (dbTargets.Count > 0)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"    [yellow]SQL Server databases ({dbTargets.Count}):[/]");
            foreach (var t in dbTargets)
                AnsiConsole.MarkupLine($"      [grey50]{Markup.Escape(t.Database)} @ {Markup.Escape(t.Server)}[/]");
        }
        if (sqliteTargets.Count > 0)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"    [yellow]SQLite databases ({sqliteTargets.Count}):[/]");
            foreach (var t in sqliteTargets)
                AnsiConsole.MarkupLine($"      [grey50]{Markup.Escape(t.DatabaseFile)} ({Markup.Escape(t.Project)})[/]");
        }
        if (appData.Exists(appData.Source))
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("    [yellow]AppData settings:[/]");
            AnsiConsole.MarkupLine($"      [grey50]{Markup.Escape(appData.Source)}[/]");
        }
        AnsiConsole.WriteLine();

        if (!AnsiConsole.Confirm("  Start backup?", defaultValue: false))
        {
            Screen.Notice("[grey50]Backup cancelled.[/]");
            Screen.PressAnyKey();
            return;
        }

        AnsiConsole.WriteLine();

        BackupResult? result = null;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .Start("  Backing up files...", ctx =>
            {
                // Spectre runs the spinner on a separate thread; the delegate
                // is the work, so calling backup.Run inline is correct — the
                // earlier Task.Run+Wait just bounced through the thread pool
                // and wrapped any failure in AggregateException. Catch here so
                // a missing robocopy can't take down the menu.
                try
                {
                    result = backup.Run(
                        target,
                        onTick: bytes => ctx.Status($"  Backing up files... [grey50]{FormatBytes(bytes)}[/]"));
                }
                catch (Exception ex)
                {
                    result = new BackupResult(false, -1, sw.Elapsed, target, Output: ex.Message);
                }
            });

        // Database backups run regardless of the file outcome — they write into
        // the same dated folder, and a file-copy hiccup shouldn't skip the SQL
        // snapshots (or vice-versa).
        var dbResults = new List<DatabaseBackupResult>();
        string? dbError = null;
        if (dbTargets.Count > 0)
        {
            AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .Start("  Backing up databases...", ctx =>
                {
                    try
                    {
                        dbResults = sql.Backup(
                            dbTargets, target,
                            onDone: r => ctx.Status($"  Backing up databases... [grey50]{Markup.Escape(r.Database)}[/]"))
                            .ToList();
                    }
                    catch (Exception ex)
                    {
                        // Capture, don't print: writing markup inside a live
                        // Status display fights it for the cursor and gets erased.
                        dbError = ex.Message;
                    }
                });
        }

        var sqliteResults = new List<SqliteBackupResult>();
        string? sqliteError = null;
        if (sqliteTargets.Count > 0)
        {
            AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .Start("  Backing up SQLite databases...", ctx =>
                {
                    try
                    {
                        sqliteResults = sqlite.Backup(
                            sqliteTargets, target,
                            onDone: r => ctx.Status($"  Backing up SQLite databases... [grey50]{Markup.Escape(r.Project)}[/]"))
                            .ToList();
                    }
                    catch (Exception ex)
                    {
                        sqliteError = ex.Message;
                    }
                });
        }

        AppDataBackupResult? appDataResult = null;
        AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .Start("  Backing up AppData settings...", ctx =>
            {
                try
                {
                    appDataResult = appData.Zip(target);
                }
                catch (Exception ex)
                {
                    appDataResult = new AppDataBackupResult(false, AppDataBackupService.ResolveZipPath(target), 0, TimeSpan.Zero, ex.Message);
                }
            });

        AnsiConsole.WriteLine();
        ReportFileResult(result);
        if (dbError is not null)
            AnsiConsole.MarkupLine($"  [red]Database backup error: {Markup.Escape(dbError)}[/]");
        ReportDatabaseResults(dbResults);
        if (sqliteError is not null)
            AnsiConsole.MarkupLine($"  [red]SQLite backup error: {Markup.Escape(sqliteError)}[/]");
        ReportSqliteResults(sqliteResults);
        ReportAppDataResult(appDataResult);

        Screen.PressAnyKey();
    }

    private static void ReportFileResult(BackupResult? result)
    {
        if (result is null)
        {
            AnsiConsole.MarkupLine("  [red]File backup did not run.[/]");
        }
        else if (result.Ok)
        {
            AnsiConsole.MarkupLine($"  [green]Files backed up — {FormatBytes(result.BytesCopied)} in {Format(result.Elapsed)}[/]");
            AnsiConsole.MarkupLine($"    [grey50]To: {Markup.Escape(result.TargetFolder)}[/]");
        }
        else
        {
            AnsiConsole.MarkupLine($"  [red]File backup failed (robocopy exit {result.RobocopyExitCode}) after {Format(result.Elapsed)}[/]");
            if (!string.IsNullOrWhiteSpace(result.Output))
            {
                foreach (var line in result.Output.Split('\n'))
                    if (!string.IsNullOrWhiteSpace(line))
                        AnsiConsole.MarkupLine($"    [grey50]{Markup.Escape(line.TrimEnd())}[/]");
            }
        }
    }

    private static void ReportDatabaseResults(IReadOnlyList<DatabaseBackupResult> results)
    {
        if (results.Count == 0) return;

        AnsiConsole.WriteLine();
        var okCount = results.Count(r => r.Ok);
        AnsiConsole.MarkupLine($"  [yellow]Databases: {okCount}/{results.Count} backed up[/]");
        foreach (var r in results)
        {
            if (r.Ok)
            {
                AnsiConsole.MarkupLine(
                    $"    [green]OK[/] [grey50]{Markup.Escape(r.Database)} @ {Markup.Escape(r.Server)} ({Format(r.Elapsed)}) -> {Markup.Escape(r.BackupFile)}[/]");
            }
            else
            {
                AnsiConsole.MarkupLine(
                    $"    [red]FAILED {Markup.Escape(r.Database)} @ {Markup.Escape(r.Server)} (sqlcmd exit {r.ExitCode})[/]");
                foreach (var line in r.Output.Split('\n'))
                    if (!string.IsNullOrWhiteSpace(line))
                        AnsiConsole.MarkupLine($"      [grey50]{Markup.Escape(line.TrimEnd())}[/]");
            }
        }
    }

    private static void ReportSqliteResults(IReadOnlyList<SqliteBackupResult> results)
    {
        if (results.Count == 0) return;

        AnsiConsole.WriteLine();
        var okCount = results.Count(r => r.Ok);
        AnsiConsole.MarkupLine($"  [yellow]SQLite databases: {okCount}/{results.Count} backed up[/]");
        foreach (var r in results)
        {
            if (r.Ok)
            {
                AnsiConsole.MarkupLine(
                    $"    [green]OK[/] [grey50]{Markup.Escape(r.Project)}: {Markup.Escape(r.DatabaseFile)} ({Format(r.Elapsed)}) -> {Markup.Escape(r.BackupFile)}[/]");
            }
            else
            {
                AnsiConsole.MarkupLine(
                    $"    [red]FAILED {Markup.Escape(r.Project)}: {Markup.Escape(r.DatabaseFile)} (sqlite3 exit {r.ExitCode})[/]");
                foreach (var line in r.Output.Split('\n'))
                    if (!string.IsNullOrWhiteSpace(line))
                        AnsiConsole.MarkupLine($"      [grey50]{Markup.Escape(line.TrimEnd())}[/]");
            }
        }
    }

    private static void ReportAppDataResult(AppDataBackupResult? result)
    {
        AnsiConsole.WriteLine();
        if (result is null)
        {
            AnsiConsole.MarkupLine("  [red]AppData backup did not run.[/]");
        }
        else if (result.Ok)
        {
            AnsiConsole.MarkupLine($"  [green]AppData settings backed up — {FormatBytes(result.Bytes)} in {Format(result.Elapsed)}[/]");
            AnsiConsole.MarkupLine($"    [grey50]To: {Markup.Escape(result.ZipPath)}[/]");
            if (!string.IsNullOrWhiteSpace(result.Output))
                AnsiConsole.MarkupLine($"    [grey50]{Markup.Escape(result.Output)}[/]");
        }
        else
        {
            AnsiConsole.MarkupLine($"  [red]AppData backup failed: {Markup.Escape(result.Output)}[/]");
        }
    }

    internal static string Format(TimeSpan t) =>
        t.TotalHours >= 1
            ? t.ToString(@"h\:mm\:ss")
            : t.ToString(@"m\:ss");

    internal static string FormatBytes(long bytes) =>
        bytes switch
        {
            < 1024L                => $"{bytes} B",
            < 1024L * 1024        => $"{bytes / 1024.0:F2} KB",
            < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F2} MB",
            _                     => $"{bytes / (1024.0 * 1024 * 1024):F2} GB"
        };
}
