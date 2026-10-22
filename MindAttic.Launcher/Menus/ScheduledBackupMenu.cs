using MindAttic.Launcher.Ui;
using Spectre.Console;

namespace MindAttic.Launcher.Menus;

/// <summary>
/// Pointer screen: Launcher no longer registers a nightly backup Scheduled Task. The job lives in
/// the MindAttic scheduler project (formerly MindAttic.WatchDog, M.WD-T6), so manage it there.
/// </summary>
public sealed class ScheduledBackupMenu
{
    public void Run()
    {
        Screen.Header("Scheduled Backup");
        AnsiConsole.MarkupLine("  Launcher no longer registers a nightly backup task.");
        AnsiConsole.MarkupLine("  The nightly backup job now lives in [cyan]MindAttic.WatchDog[/] (now MindAttic.Scheduler, ticket M.WD-T6).");
        AnsiConsole.MarkupLine("  Manage its schedule, status and run-now there. To back up manually, use the [cyan]Backup[/] menu.");
        AnsiConsole.WriteLine();
        Screen.PressAnyKey();
    }
}
