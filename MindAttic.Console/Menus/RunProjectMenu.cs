using MindAttic.Console.Models;
using MindAttic.Console.Services;
using MindAttic.Console.Ui;
using Spectre.Console;

namespace MindAttic.Console.Menus;

public sealed class RunProjectMenu(SettingsStore store, WindowsTerminalLauncher wt)
{
    public void Run()
    {
        while (true)
        {
            var settings = store.Load();
            var items = ProjectRoster.Sorted(settings)
                .Where(p => !string.IsNullOrWhiteSpace(p.RunCommand))
                .Select(p => new MenuItem
                {
                    Name = p.Name,
                    Description = p.RunCommand,
                    Tag = p
                })
                .ToList();

            Screen.Header("Run Project");
            if (items.Count == 0)
            {
                Screen.Notice("[grey50]No projects have a RunCommand configured.[/]");
                Screen.PressAnyKey();
                return;
            }

            var sel = Menu.Prompt("Choose a project to run:", items);
            if (sel is null) return;

            var project = (Project)sel.Tag!;
            if (!Directory.Exists(project.Path))
            {
                // wt opened with -d <missing path> fails to spawn the tab and the
                // user sees nothing happen — worse, the "Started" notice would lie.
                // Say why instead, matching OpenProjectMenu's path-not-found guard.
                Screen.Notice($"[red]Path not found:[/] [grey50]{Markup.Escape(project.Path)}[/]");
                Screen.PressAnyKey();
                continue;
            }
            wt.Open(wt.BuildRunCommandTab(project));
            Screen.Notice($"[green]Started:[/] [cyan1]{Markup.Escape(project.Name)}[/] → {Markup.Escape(project.RunCommand!)}");
            Thread.Sleep(800);
        }
    }
}
