using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using MindAttic.Launcher.Menus;
using MindAttic.Launcher.Models;
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

        var (exit, _) = Run(app, () => TopicRepoSync.FetchOrNull(), settings.Project, settings.Message,
            new WorkspaceCommitter(git, ExePath.WorkspaceRoot), r => AnsiConsole.MarkupLine(CommitMenu.Format(r)));
        return exit;
    }

    /// <summary>
    /// The command's logic with its collaborators passed in. A specific --project always resolves by name, topic
    /// status aside: an explicit target that silently no-ops because it fell off the tagged list would violate
    /// M.LNCH-LAW-1 (fail loudly, not quietly), so an unknown name throws. The "commit everything" default is
    /// where the topic decides the roster. A failing repo makes the exit code 1 but never stops the rest.
    /// </summary>
    public static (int ExitCode, IReadOnlyList<ProjectCommitResult> Results) Run(
        AppSettings app, Func<IReadOnlySet<string>?> taggedFullNames, string? project, string? message,
        WorkspaceCommitter committer, Action<ProjectCommitResult>? report = null)
    {
        var targets = string.IsNullOrWhiteSpace(project)
            ? ProjectRoster.Sorted(app, taggedFullNames())
            : new[] { ProjectRoster.FindByName(app, project!) ?? throw new InvalidOperationException($"Unknown project: {project}") };

        var results = committer.Run(targets, message, report);
        return (results.All(r => r.Ok) ? 0 : 1, results);
    }
}
