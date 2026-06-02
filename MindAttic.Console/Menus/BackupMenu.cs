using MindAttic.Console.Services;
using MindAttic.Console.Ui;
using Spectre.Console;

namespace MindAttic.Console.Menus;

public sealed class BackupMenu(BackupService backup, SettingsStore store, SqlBackupService sql)
{
    public BackupMenu() : this(new BackupService(), new SettingsStore(), new SqlBackupService()) { }

    public void Run()
    {
        Screen.Header("Backup");

        var target = backup.ResolveTargetFolder();

        // A settings read failure must not block the file backup — degrade to
        // "no databases" and warn, rather than aborting the whole backup because
        // the roster couldn't be loaded.
        IReadOnlyList<BackupTarget> dbTargets;
        try
        {
            dbTargets = SqlBackupService.CollectTargets(store.Load());
        }
        catch (Exception ex)
        {
            dbTargets = [];
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
            AnsiConsole.MarkupLine($"    [yellow]Databases ({dbTargets.Count}):[/]");
            foreach (var t in dbTargets)
                AnsiConsole.MarkupLine($"      [grey50]{Markup.Escape(t.Database)} @ {Markup.Escape(t.Server)}[/]");
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
                        onTick: elapsed => ctx.Status($"  Backing up files... [grey50]{Format(elapsed)}[/]"));
                }
                catch (Exception ex)
                {
                    result = new BackupResult(false, -1, sw.Elapsed, target, ex.Message);
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

        AnsiConsole.WriteLine();
        ReportFileResult(result);
        if (dbError is not null)
            AnsiConsole.MarkupLine($"  [red]Database backup error: {Markup.Escape(dbError)}[/]");
        ReportDatabaseResults(dbResults);

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
            AnsiConsole.MarkupLine($"  [green]Files backed up in {Format(result.Elapsed)}[/]");
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

    private static string Format(TimeSpan t) =>
        t.TotalHours >= 1
            ? t.ToString(@"h\:mm\:ss")
            : t.ToString(@"m\:ss");
}
