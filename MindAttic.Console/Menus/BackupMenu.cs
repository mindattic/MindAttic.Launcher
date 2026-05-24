using MindAttic.Console.Services;
using MindAttic.Console.Ui;
using Spectre.Console;

namespace MindAttic.Console.Menus;

public sealed class BackupMenu(BackupService backup)
{
    public BackupMenu() : this(new BackupService()) { }

    public void Run()
    {
        Screen.Header("Backup");

        var target = backup.ResolveTargetFolder();
        AnsiConsole.MarkupLine("  You are about to back up:");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"    [yellow]From:[/] {Markup.Escape(backup.Source)}");
        AnsiConsole.MarkupLine($"    [yellow]To:  [/] {Markup.Escape(target)}");
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
            .Start("  Backing up...", ctx =>
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
                        onTick: elapsed => ctx.Status($"  Backing up... [grey50]{Format(elapsed)}[/]"));
                }
                catch (Exception ex)
                {
                    result = new BackupResult(false, -1, sw.Elapsed, target, ex.Message);
                }
            });

        AnsiConsole.WriteLine();
        if (result is null)
        {
            AnsiConsole.MarkupLine("  [red]Backup did not run.[/]");
        }
        else if (result.Ok)
        {
            AnsiConsole.MarkupLine($"  [green]Backup complete in {Format(result.Elapsed)}[/]");
            AnsiConsole.MarkupLine($"    [grey50]To: {Markup.Escape(result.TargetFolder)}[/]");
        }
        else
        {
            AnsiConsole.MarkupLine($"  [red]Backup failed (robocopy exit {result.RobocopyExitCode}) after {Format(result.Elapsed)}[/]");
            if (!string.IsNullOrWhiteSpace(result.Output))
            {
                foreach (var line in result.Output.Split('\n'))
                    if (!string.IsNullOrWhiteSpace(line))
                        AnsiConsole.MarkupLine($"    [grey50]{Markup.Escape(line.TrimEnd())}[/]");
            }
        }

        Screen.PressAnyKey();
    }

    private static string Format(TimeSpan t) =>
        t.TotalHours >= 1
            ? t.ToString(@"h\:mm\:ss")
            : t.ToString(@"m\:ss");
}
