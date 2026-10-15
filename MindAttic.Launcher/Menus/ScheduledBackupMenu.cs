using MindAttic.Launcher.Services;
using MindAttic.Launcher.Ui;
using Spectre.Console;

namespace MindAttic.Launcher.Menus;

/// <summary>
/// Status and toggle for the nightly backup Scheduled Task (see
/// <see cref="Services.ScheduledTaskService"/>), so what's going to fire and when is visible
/// inside Launcher instead of only in Task Scheduler's own UI.
/// </summary>
public sealed class ScheduledBackupMenu(ScheduledTaskService tasks)
{
    public ScheduledBackupMenu() : this(new ScheduledTaskService()) { }

    public void Run()
    {
        while (true)
        {
            Screen.Header("Scheduled Backup");
            var status = tasks.Query();
            RenderStatus(status);

            var items = new List<MenuItem>();
            if (!status.Registered)
            {
                items.Add(new() { Name = "Register", Description = "create the nightly backup task (daily, 00:00, keeps the newest 30)", Tag = "register" });
            }
            else
            {
                items.Add(status.Enabled
                    ? new() { Name = "Disable", Description = "stop it firing (keeps the task, doesn't delete it)", Tag = "disable" }
                    : new() { Name = "Enable", Description = "resume firing on its schedule", Tag = "enable" });
                items.Add(new() { Name = "Run now", Description = "fire it immediately, for testing", Tag = "run" });
                items.Add(new() { Name = "Re-register", Description = "reapply time/retention (prompts for both)", Tag = "register" });
                items.Add(new() { Name = "Remove", Description = "delete the task entirely", Tag = "remove" });
            }

            var selected = Ui.Menu.Prompt("Scheduled Backup — choose an action:", items);
            if (selected is null) return;

            switch (selected.Tag)
            {
                case "enable":
                case "disable":
                {
                    var (ok, output) = tasks.SetEnabled(Equals(selected.Tag, "enable"));
                    Report(ok, output);
                    break;
                }
                case "run":
                {
                    var (ok, output) = tasks.RunNow();
                    Report(ok, output);
                    break;
                }
                case "remove":
                {
                    if (!AnsiConsole.Confirm("  Delete the scheduled task?", defaultValue: false)) break;
                    var (ok, output) = tasks.Unregister();
                    Report(ok, output);
                    break;
                }
                case "register":
                {
                    var time = AnsiConsole.Ask("  Daily run time (HH:mm)?", "00:00");
                    var keep = AnsiConsole.Ask("  Keep how many backups?", 30);
                    var (ok, output) = tasks.Register(time, keep);
                    Report(ok, output);
                    break;
                }
            }
        }
    }

    private static void RenderStatus(ScheduledTaskStatus status)
    {
        if (!status.Registered)
        {
            AnsiConsole.MarkupLine("  [yellow]Not registered.[/] No nightly backup is scheduled.");
            AnsiConsole.WriteLine();
            return;
        }

        AnsiConsole.MarkupLine($"  State: {(status.Enabled ? "[green]Enabled[/]" : "[grey50]Disabled[/]")}");
        AnsiConsole.MarkupLine($"  Last run: {Describe(status.LastRunTime)}" +
            (status.LastTaskResult is { } code ? $"  [grey50](exit {code})[/]" : ""));
        AnsiConsole.MarkupLine($"  Next run: {Describe(status.NextRunTime)}");
        AnsiConsole.WriteLine();
    }

    private static string Describe(DateTime? t) =>
        t is null ? "[grey50](unknown)[/]" : Markup.Escape(t.Value.ToString("yyyy-MM-dd HH:mm"));

    private static void Report(bool ok, string output)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine(ok ? "  [green]Done.[/]" : $"  [red]Failed:[/] {Markup.Escape(output)}");
        Screen.PressAnyKey();
    }
}
