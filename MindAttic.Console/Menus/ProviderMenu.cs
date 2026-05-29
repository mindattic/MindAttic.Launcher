using MindAttic.Console.Models;
using MindAttic.Console.Services;
using MindAttic.Console.Ui;
using Spectre.Console;

namespace MindAttic.Console.Menus;

public sealed class ProviderMenu(SettingsStore store, AgentProviderRegistry providers)
{
    public void Run()
    {
        while (true)
        {
            var settings = store.Load();
            var defaultKey = providers.CurrentDefaultKey();

            var items = new List<MenuItem>
            {
                new() { Name = "Default Provider", Description = defaultKey, Tag = "default" }
            };
            foreach (var p in ProjectRoster.Sorted(settings))
            {
                var label = string.IsNullOrWhiteSpace(p.Provider) ? $"default: {defaultKey}" : p.Provider!;
                items.Add(new MenuItem { Name = p.Name, Description = label, Tag = p });
            }

            Screen.Header("Provider");
            var sel = Menu.Prompt("Choose what to configure:", items);
            if (sel is null) return;

            switch (sel.Tag)
            {
                case "default":
                    PickDefaultProvider();
                    break;
                case Project project:
                    PickProjectProvider(project);
                    break;
            }
        }
    }

    private void PickDefaultProvider()
    {
        var currentKey = providers.CurrentDefaultKey();
        var items = providers.All()
            .Select(p => new MenuItem
            {
                Name = p.Name,
                Description = string.Equals(p.Key, currentKey, StringComparison.OrdinalIgnoreCase)
                    ? $"current - {p.RunCommand}"
                    : p.RunCommand,
                Tag = p
            })
            .ToList();

        Screen.Header("Provider", "Default");
        var sel = Menu.Prompt("Pick the default provider for all projects:", items);
        if (sel is null) return;

        providers.SetDefault(((AgentProvider)sel.Tag!).Key);
        Screen.Notice($"[green]Default provider set to[/] [cyan1]{Markup.Escape(((AgentProvider)sel.Tag!).Name)}[/]");
        Thread.Sleep(600);
    }

    private void PickProjectProvider(Project project)
    {
        var defaultProvider = providers.Current();
        var projectKey = string.IsNullOrWhiteSpace(project.Provider) ? null : project.Provider;

        var items = new List<MenuItem>
        {
            new()
            {
                Name = "Use Default",
                Description = projectKey is null
                    ? $"current - use default: {defaultProvider.Name}"
                    : $"use default: {defaultProvider.Name}",
                Tag = "default"
            }
        };
        foreach (var p in providers.All())
        {
            items.Add(new MenuItem
            {
                Name = p.Name,
                Description = string.Equals(p.Key, projectKey, StringComparison.OrdinalIgnoreCase)
                    ? $"current - {p.RunCommand}"
                    : p.RunCommand,
                Tag = p
            });
        }

        Screen.Header("Provider", project.Name);
        var sel = Menu.Prompt($"Pick a provider for {Markup.Escape(project.Name)}:", items);
        if (sel is null) return;

        if (sel.Tag is AgentProvider chosen)
        {
            providers.SetProjectProvider(project.Name, chosen.Key);
            Screen.Notice($"[green]{Markup.Escape(project.Name)} provider set to[/] [cyan1]{Markup.Escape(chosen.Name)}[/]");
        }
        else
        {
            providers.SetProjectProvider(project.Name, null);
            Screen.Notice($"[green]{Markup.Escape(project.Name)} reset to default provider.[/]");
        }
        Thread.Sleep(600);
    }
}
