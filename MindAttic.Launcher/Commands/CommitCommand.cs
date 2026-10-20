using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using MindAttic.Launcher.Menus;
using MindAttic.Launcher.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace MindAttic.Launcher.Commands;

/// <summary>
/// Out-of-menu commit subcommand. <c>mindattic commit</c> commits + pushes
/// every project; <c>mindattic commit --project Name</c> targets one project;
/// <c>--message "..."</c> overrides the auto-generated message. Each commit is
/// aligned to a tickets ticket (see <see cref="WorkspaceCommitter"/>).
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
        [Description("Commit message (defaults to auto-generated from status). Naming one of the repo's tickets (e.g. M.LNCH-T21) aligns to it; otherwise a closed ticket is created.")]
        public string? Message { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var store = new SettingsStore();
        var git = new GitService();

        // Same backfill as the interactive menu (MainMenuCommand): a project
        // registered before RepoUrl was captured can't be matched against the
        // tagged set and would silently drop out of "commit everything".
        RepoUrlBackfill.Run(store, git.RemoteUrl);

        var app = store.Load();

        // A specific --project always resolves by name, topic status aside —
        // an explicit target that silently no-ops because it fell off the
        // tagged list would violate M.LNCH-LAW-1 (fail loudly, not quietly). The
        // "commit everything" default is where the topic decides the roster.
        var targets = string.IsNullOrWhiteSpace(settings.Project)
            ? ProjectRoster.Sorted(app, TopicRepoSync.FetchOrNull())
            : new[] { ProjectRoster.FindByName(app, settings.Project!) ?? throw new InvalidOperationException($"Unknown project: {settings.Project}") };

        var results = new WorkspaceCommitter(git, ExePath.WorkspaceRoot)
            .Run(targets, settings.Message, r => AnsiConsole.MarkupLine(CommitMenu.Format(r)));
        return results.All(r => r.Ok) ? 0 : 1;
    }
}
