using MindAttic.Launcher.Models;
using MindAttic.Launcher.Services;
using MindAttic.Launcher.Ui;
using Spectre.Console;

namespace MindAttic.Launcher.Menus;

public sealed class CommitMenu(SettingsStore store, GitService git, IReadOnlySet<string>? tagged = null)
{
    public void Run()
    {
        var resumeIndex = 0;
        while (true)
        {
            var sortedProjects = ProjectRoster.Sorted(store.Load(), tagged);
            var statuses = git.FetchShortStatuses(sortedProjects);

            var items = new List<MenuItem>
            {
                new()
                {
                    Name = "All Projects",
                    Description = "commit and push across all projects",
                    Tag = "all"
                }
            };

            foreach (var p in sortedProjects)
                items.Add(new MenuItem { Name = p.Name, Description = statuses[p.Name], Tag = p });

            Screen.Header("Commit and sync");
            var result = Menu.PromptWithKeys("Choose a project to commit:", items, customKeys: null, initialIndex: resumeIndex);
            resumeIndex = result.Index;
            var sel = result.Selected;
            if (sel is null) return;

            if (Equals(sel.Tag, "all"))
                CommitAll(sortedProjects);
            else if (sel.Tag is Project project)
                CommitOne(project);
        }
    }

    private void CommitAll(IReadOnlyList<Project> projects)
    {
        Screen.Header("Commit and sync", "All Projects");
        AnsiConsole.MarkupLine("  [yellow]Enter a commit message, leave blank to auto-generate, or Esc to cancel.[/]");
        AnsiConsole.WriteLine();
        var userMessage = Prompt.TextOrCancel("  > ");
        AnsiConsole.WriteLine();
        if (userMessage is null) return;

        Commit(projects, userMessage);

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("  [green]Done.[/]");
        Screen.PressAnyKey();
    }

    private void CommitOne(Project project)
    {
        Screen.Header("Commit and sync", project.Name);
        AnsiConsole.MarkupLine("  [yellow]Enter a commit message, leave blank to auto-generate, or Esc to cancel.[/]");
        AnsiConsole.WriteLine();
        var userMessage = Prompt.TextOrCancel("  > ");
        AnsiConsole.WriteLine();
        if (userMessage is null) return;

        Commit([project], userMessage);

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("  [green]Done.[/]");
        Screen.PressAnyKey();
    }

    private void Commit(IReadOnlyList<Project> projects, string? userMessage) =>
        new WorkspaceCommitter(git, ExePath.WorkspaceRoot)
            .Run(projects, userMessage, r => AnsiConsole.MarkupLine("    " + Format(r)));

    /// <summary>One result line, shared with <c>mindattic commit</c>.</summary>
    public static string Format(ProjectCommitResult r)
    {
        var name = $"[white]{Markup.Escape(r.Project)}:[/]";
        if (r.Ok && r.Detail == "nothing to commit") return $"{name} [grey50]nothing to commit[/]";
        var label = string.IsNullOrEmpty(r.CommitMessage) ? "" : $" [darkorange]{Markup.Escape(r.CommitMessage)}[/]";
        var ticket = r.TicketCreated ? $" [cyan]new ticket {Markup.Escape(r.TicketId!)}[/]" : "";
        var color = !r.Ok ? "red" : r.Detail == GitService.ArchivedNotice ? "yellow" : "green";
        var detail = $"[{color}]{Markup.Escape(r.Detail)}[/]";
        return $"{name}{label}{ticket} {detail}";
    }
}
