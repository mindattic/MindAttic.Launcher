using System.Diagnostics;
using MindAttic.Launcher.Ui;
using Spectre.Console;

namespace MindAttic.Launcher.Menus;

/// <summary>
/// Pointer screen: Launcher no longer registers a nightly backup Scheduled Task. The job lives in
/// MindAttic.WatchDog (M.WD-T6; the project is now named MindAttic.Scheduler), so manage it there.
/// If a legacy task from an earlier Launcher is still registered it would double the nightly
/// backup, so this screen detects it and offers to delete it.
/// </summary>
public sealed class ScheduledBackupMenu
{
    private const string TaskName = "MindAtticLauncherNightlyBackup";
    private static readonly string[] LegacyPaths = [@"\MindAttic\Launcher\" + TaskName, @"\" + TaskName];

    public void Run()
    {
        Screen.Header("Scheduled Backup");
        AnsiConsole.MarkupLine("  Launcher no longer registers a nightly backup task.");
        AnsiConsole.MarkupLine("  The nightly backup job now lives in [cyan]MindAttic.WatchDog[/] (M.WD-T6). Manage its schedule, status and run-now there.");
        AnsiConsole.MarkupLine("  To back up manually, use the [cyan]Backup[/] menu.");
        AnsiConsole.WriteLine();

        foreach (var path in LegacyPaths.Where(Exists))
        {
            AnsiConsole.MarkupLine($"  [yellow]Legacy task still registered:[/] {Markup.Escape(path)}");
            AnsiConsole.MarkupLine("  It would run a second nightly backup alongside the WatchDog job.");
            if (AnsiConsole.Confirm("  Delete the legacy task?", defaultValue: true))
            {
                var code = Schtasks("/Delete", "/TN", path, "/F");
                AnsiConsole.MarkupLine(code == 0 ? "  [green]Deleted.[/]"
                    : $"  [red]Failed (exit {code}).[/] Run: Unregister-ScheduledTask -TaskName {TaskName} -Confirm:$false");
            }
            AnsiConsole.WriteLine();
        }

        Screen.PressAnyKey();
    }

    private static bool Exists(string path) => Schtasks("/Query", "/TN", path) == 0;

    private static int Schtasks(params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo("schtasks.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi);
            if (p is null) return -1;
            p.StandardOutput.ReadToEnd();
            p.StandardError.ReadToEnd();
            p.WaitForExit();
            return p.ExitCode;
        }
        catch { return -1; }
    }
}
