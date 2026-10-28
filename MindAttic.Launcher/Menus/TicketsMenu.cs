using MindAttic.Launcher.Services;
using MindAttic.Launcher.Ui;
using MindAttic.Tickets;
using MindAttic.Tickets.Repos;
using MindAttic.Tickets.Log;
using Spectre.Console;

namespace MindAttic.Launcher.Menus;

/// <summary>
/// The Launcher's front door to MindAttic.Tickets (HOUSE-LAW-6): open tickets across the
/// workspace, each repo's local-only work, and the ticket-log checks. Read-only on purpose —
/// deleting folders (<c>tickets repos prune</c>) stays in the CLI, where it has a preview and a
/// typed confirmation.
/// </summary>
public sealed class TicketsMenu(string workspaceRoot)
{
    public void Run()
    {
        var items = new List<MenuItem>
        {
            new() { Name = "Open tickets",  Description = "every open ticket across the workspace", Tag = "open" },
            new() { Name = "Repo status",   Description = "uncommitted, unpushed and local-only work per repo, plus GitHub state", Tag = "status" },
            new() { Name = "Ticket checks", Description = "duplicate project codes, missing or malformed ticket logs", Tag = "check" },
        };

        var resumeIndex = 0;
        while (true)
        {
            Screen.Header("Tickets");
            var result = Menu.PromptWithKeys("Choose a view:", items, initialIndex: resumeIndex);
            resumeIndex = result.Index;
            if (result.Selected is null) return;

            var repos = new Workspace(workspaceRoot).Repos();
            switch (result.Selected.Tag)
            {
                case "open":   ShowOpenTickets(repos); break;
                case "status": ShowStatus(repos); break;
                case "check":  ShowChecks(repos); break;
            }
            Screen.PressAnyKey();
        }
    }

    private static void ShowOpenTickets(IReadOnlyList<Repo> repos)
    {
        Screen.Header("Tickets", "Open tickets");
        var open = WorkspaceTickets.All(repos).Where(x => x.Ticket.Status == TicketRules.Open).ToList();
        if (open.Count == 0) { AnsiConsole.MarkupLine("  [green]No open tickets.[/]"); return; }

        var table = new Table().Border(TableBorder.Rounded).AddColumns("Repo", "ID", "Date", "Type", "Summary");
        foreach (var x in open)
            table.AddRow(Markup.Escape(x.Repo.Name), Markup.Escape(x.Ticket.Id), Markup.Escape(x.Ticket.Date),
                Markup.Escape(x.Ticket.Type), Markup.Escape(x.Ticket.Summary));
        AnsiConsole.Write(table);
    }

    private static void ShowStatus(IReadOnlyList<Repo> repos)
    {
        Screen.Header("Tickets", "Repo status");
        AnsiConsole.MarkupLine("  [grey]Reading git state…[/]");
        var statuses = repos.Select(RepoStatus.Read).ToList();

        IReadOnlyDictionary<string, GitHubRepoInfo>? github = null;
        var token = GitHubCredentials.GetToken();
        if (!string.IsNullOrWhiteSpace(token))
        {
            try
            {
                var client = new GitHubRepoIndex();
                var index = client.FetchAsync(token).GetAwaiter().GetResult();
                github = client.WithRenamesAsync(token, index, statuses.Select(s => s.GitHubSlug).OfType<string>()).GetAwaiter().GetResult();
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
            {
                AnsiConsole.MarkupLine($"  [yellow]Couldn't read GitHub ({Markup.Escape(ex.Message)}); GitHub column is blank.[/]");
            }
        }

        var table = new Table().Border(TableBorder.Rounded).AddColumns("Repo", "GitHub", "Uncommitted", "Unpushed", "Stash", "Local-only ignored");
        foreach (var s in statuses)
            table.AddRow(Markup.Escape(s.Repo.Name), GitHubCell(s, github),
                Count(s.Uncommitted), Count(s.Unpushed), Count(s.Stashes),
                s.LocalOnlyIgnored.Count == 0 ? "[grey]—[/]" : $"[yellow]{s.LocalOnlyIgnored.Count}[/]");
        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine("  [grey]To remove archived or untagged repos: tickets repos prune (preview), then --apply.[/]");
    }

    private static void ShowChecks(IReadOnlyList<Repo> repos)
    {
        Screen.Header("Tickets", "Ticket checks");
        var problems = WorkspaceTickets.Check(repos);
        if (problems.Count == 0) { AnsiConsole.MarkupLine("  [green]No problems.[/]"); return; }
        foreach (var p in problems)
            AnsiConsole.MarkupLine($"  [yellow]{Markup.Escape(p.Repo)}:[/] {Markup.Escape(p.Message)}");
    }

    private static string Count(int n) => n == 0 ? "[grey]0[/]" : $"[yellow]{n}[/]";

    internal static string GitHubCell(RepoStatus s, IReadOnlyDictionary<string, GitHubRepoInfo>? github)
    {
        if (s.Error is not null) return "[red]git error[/]";
        if (s.GitHubSlug is null) return "[red]no GitHub remote[/]";
        if (github is null) return "[grey]?[/]";
        if (!github.TryGetValue(s.GitHubSlug, out var info)) return "[red]not found[/]";
        if (info.Archived) return "[grey]archived[/]";
        return info.HasTopic(GitHubRepoIndex.WorkInProgressTopic) ? "[green]wip[/]" : "[yellow]untagged[/]";
    }
}
