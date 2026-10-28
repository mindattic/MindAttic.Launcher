using MindAttic.Launcher.Models;
using MindAttic.Launcher.Services;
using MindAttic.Launcher.Ui;
using Spectre.Console;

namespace MindAttic.Launcher.Menus;

public sealed class ProjectSetupMenu(SettingsStore store, string projectName)
{
    public void Run()
    {
        var resumeIndex = 0;
        while (true)
        {
            var settings = store.Load();
            var project = ProjectRoster.FindByName(settings, projectName);
            if (project is null) return;

            var items = new List<MenuItem>
            {
                new()
                {
                    Name = "Alias",
                    Description = string.IsNullOrWhiteSpace(project.TabAlias) ? "(uses project name)" : project.TabAlias,
                    Tag = "alias"
                },
                new()
                {
                    Name = "Description",
                    Description = string.IsNullOrWhiteSpace(project.Description) ? "(none)" : project.Description,
                    Tag = "desc"
                },
                new()
                {
                    Name = "Color Scheme",
                    Description = string.IsNullOrWhiteSpace(project.ColorScheme) ? "(none)" : project.ColorScheme,
                    Tag = "scheme"
                },
                new()
                {
                    Name = "Tab Color",
                    Description = string.IsNullOrWhiteSpace(project.TabColor) ? "(none)" : project.TabColor,
                    Tag = "color"
                },
            };

            Screen.Header(projectName, "Setup");
            var result = Menu.PromptWithKeys($"Configure {Markup.Escape(projectName)}:", items, initialIndex: resumeIndex);
            resumeIndex = result.Index;
            var sel = result.Selected;
            if (sel is null) return;

            switch (sel.Tag)
            {
                case "alias":    EditField(project, "Alias",        p => p.TabAlias,    (p, v) => p.TabAlias    = v); break;
                case "desc":     EditField(project, "Description",  p => p.Description, (p, v) => p.Description = v); break;
                case "scheme":   EditField(project, "Color Scheme", p => p.ColorScheme, (p, v) => p.ColorScheme = v); break;
                case "color":    EditField(project, "Tab Color",    p => p.TabColor,    (p, v) => p.TabColor    = v); break;
            }
        }
    }

    private void EditField(Project project, string label, Func<Project, string?> get, Action<Project, string?> set)
    {
        var current = get(project);
        Screen.Header(projectName, "Setup", label);
        AnsiConsole.MarkupLine($"  Current: [cyan1]{Markup.Escape(string.IsNullOrWhiteSpace(current) ? "(none)" : current!)}[/]");
        AnsiConsole.WriteLine();

        // Not a Spectre TextPrompt with a default: Enter on a blank line returns the default (the
        // current value), so "blank to clear" could never clear. Blank clears; Esc cancels.
        var entered = Prompt.TextOrCancel($"  [cyan1]{Markup.Escape(label)}[/] [grey50](blank to clear, Esc to cancel):[/] ");
        if (entered is null) return;

        var trimmed = entered.Trim();
        store.Update(s =>
        {
            var p = ProjectRoster.FindByName(s, projectName);
            if (p is null) return;
            set(p, string.IsNullOrWhiteSpace(trimmed) ? null : trimmed);
        });

        Screen.Notice($"[green]{Markup.Escape(label)} saved.[/]");
        Thread.Sleep(600);
    }
}
