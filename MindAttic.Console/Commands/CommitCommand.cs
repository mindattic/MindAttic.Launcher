using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using MindAttic.Console.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace MindAttic.Console.Commands;

/// <summary>
/// Out-of-menu commit subcommand. <c>mindattic commit</c> commits + pushes
/// every project; <c>mindattic commit --project Name</c> targets one project;
/// <c>--message "..."</c> overrides the auto-generated message.
/// </summary>
[SuppressMessage("Performance", "CA1812", Justification = "Instantiated by Spectre.Console.Cli")]
public sealed class CommitCommand : Command<CommitCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("-p|--project <PROJECT>")]
        [Description("Limit to a single project (defaults to all).")]
        public string? Project { get; init; }

        [CommandOption("-m|--message <MESSAGE>")]
        [Description("Commit message (defaults to auto-generated from status).")]
        public string? Message { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var store = new SettingsStore();
        var git = new GitService();
        var app = store.Load();

        var targets = string.IsNullOrWhiteSpace(settings.Project)
            ? ProjectRoster.Sorted(app)
            : new[] { ProjectRoster.FindByName(app, settings.Project!) ?? throw new InvalidOperationException($"Unknown project: {settings.Project}") };

        var ok = true;
        foreach (var p in targets)
        {
            if (!Directory.Exists(p.Path))
            {
                AnsiConsole.MarkupLine($"[white]{Markup.Escape(p.Name)}:[/] [red]path not found[/]");
                ok = false;
                continue;
            }

            var status = git.Status(p.Path);
            if (!status.IsValid)
            {
                AnsiConsole.MarkupLine($"[white]{Markup.Escape(p.Name)}:[/] [red]{Markup.Escape(status.Error ?? "")}[/]");
                ok = false;
                continue;
            }
            if (status.IsClean)
            {
                AnsiConsole.MarkupLine($"[white]{Markup.Escape(p.Name)}:[/] [grey50]nothing to commit[/]");
                continue;
            }

            var (committed, msg, commitMessage) = git.Commit(p.Path, settings.Message);
            var label = string.IsNullOrEmpty(commitMessage) ? "" : $" [darkorange]{Markup.Escape(commitMessage)}[/]";
            if (committed)
                AnsiConsole.MarkupLine($"[white]{Markup.Escape(p.Name)}:[/]{label} [green]{Markup.Escape(msg)}[/]");
            else
            {
                AnsiConsole.MarkupLine($"[white]{Markup.Escape(p.Name)}:[/]{label} [red]{Markup.Escape(msg)}[/]");
                ok = false;
            }
        }

        return ok ? 0 : 1;
    }
}
