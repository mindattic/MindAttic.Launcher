using MindAttic.Console.Models;
using MindAttic.Console.Services;
using MindAttic.Console.Ui;
using Spectre.Console;

namespace MindAttic.Console.Menus;

public sealed class ProjectActionMenu(
    SettingsStore store,
    AgentProviderRegistry providers,
    WindowsTerminalLauncher wt,
    Project project)
{
    public void Run()
    {
        while (true)
        {
            var settings = store.Load();
            var current = ProjectRoster.FindByName(settings, project.Name) ?? project;
            var provider = providers.EffectiveProvider(current);

            var items = new List<MenuItem>
            {
                new() { Name = "Run", Description = $"open agent tab ({provider.Name})", Tag = "run" }
            };

            if (!string.IsNullOrWhiteSpace(current.RunCommand))
            {
                items.Add(new MenuItem
                {
                    Name = "Run Command",
                    Description = current.RunCommand,
                    Tag = "runcmd"
                });
            }

            items.Add(new MenuItem
            {
                Name = "Setup",
                Description = "alias, description, color, provider",
                Tag = "setup"
            });

            Screen.Header(current.Name);
            var sel = Menu.Prompt($"Choose an action for {Markup.Escape(current.Name)}:", items);
            if (sel is null) return;

            switch (sel.Tag)
            {
                case "run":
                    if (!Directory.Exists(current.Path))
                    {
                        Screen.Notice($"[red]Path not found:[/] [grey50]{Markup.Escape(current.Path)}[/]");
                        Screen.PressAnyKey();
                        break;
                    }
                    ExePath.EnsureFresh();
                    wt.Open(wt.BuildAgentTab(current, provider, ExePath.Release));
                    Thread.Sleep(800);
                    return;

                case "runcmd":
                    if (!Directory.Exists(current.Path))
                    {
                        Screen.Notice($"[red]Path not found:[/] [grey50]{Markup.Escape(current.Path)}[/]");
                        Screen.PressAnyKey();
                        break;
                    }
                    wt.Open(wt.BuildRunCommandTab(current));
                    Screen.Notice($"[green]Started:[/] [cyan1]{Markup.Escape(current.Name)}[/] → {Markup.Escape(current.RunCommand!)}");
                    Thread.Sleep(800);
                    return;

                case "setup":
                    new ProjectSetupMenu(store, providers, current.Name).Run();
                    break;
            }
        }
    }
}
