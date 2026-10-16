using System.Text;
using System.Text.RegularExpressions;
using MindAttic.Launcher.Models;
using MindAttic.Workflow;
using MindAttic.Workflow.Data;
using MindAttic.Workflow.Tickets;

namespace MindAttic.Launcher.Services;

/// <summary>What happened to one repo during a commit run.</summary>
public sealed record ProjectCommitResult(
    string Project, bool Ok, string Detail, string CommitMessage = "", string? TicketId = null, bool TicketCreated = false);

/// <summary>
/// The one commit engine behind the Commit menu and <c>mindattic commit</c> (HOUSE-LAW-6). Every commit is
/// aligned to a MindAttic.Workflow ticket: a message naming one of the repo's tickets is kept as-is, otherwise a
/// closed ticket is created from the message and its ID appended, and the repo's generated USER_STORIES.md view
/// is rewritten before staging so the new row lands in the same commit. The MindAttic.Workflow repo, which
/// holds the database, is committed last so it carries every ticket the run created.
/// </summary>
public sealed partial class WorkspaceCommitter(GitService git, string workspaceRoot, string? dbPath = null)
{
    public const string WorkflowRepoName = "MindAttic.Workflow";
    public const string SyncMessage = "Sync the workflow database";

    private static readonly HashSet<string> DocExtensions = new(StringComparer.OrdinalIgnoreCase) { ".md", ".htm", ".html", ".txt" };

    private readonly string dbPath = dbPath ?? WorkflowDb.DefaultPath(workspaceRoot);

    private string WorkflowRepoPath => Path.Combine(workspaceRoot, WorkflowRepoName);

    public IReadOnlyList<ProjectCommitResult> Run(IEnumerable<Project> projects, string? userMessage, Action<ProjectCommitResult>? report = null)
    {
        var results = new List<ProjectCommitResult>();
        void Add(ProjectCommitResult r) { results.Add(r); report?.Invoke(r); }

        var ordered = projects.OrderBy(p => IsWorkflowRepo(p.Path) ? 1 : 0).ToList();
        var created = false;
        var workflowCommitted = false;
        foreach (var p in ordered)
        {
            var r = CommitProject(p, userMessage);
            created |= r.TicketCreated;
            workflowCommitted |= IsWorkflowRepo(p.Path);
            Add(r);
        }

        // A single-repo commit still created a ticket in the shared database; push it so other machines see it.
        if (created && !workflowCommitted) Add(SyncWorkflowDb());
        return results;
    }

    private ProjectCommitResult CommitProject(Project p, string? userMessage)
    {
        if (!Directory.Exists(p.Path)) return new(p.Name, false, "path not found");

        var isWorkflow = IsWorkflowRepo(p.Path);
        if (isWorkflow) WriteDump();

        var status = git.Status(p.Path);
        if (!status.IsValid) return new(p.Name, false, status.Error ?? "");
        if (status.IsClean) return new(p.Name, true, "nothing to commit");

        if (isWorkflow && status.Changes.All(c => IsDatabaseFile(c.FileName)))
        {
            var (synced, syncDetail, _) = git.Commit(p.Path, SyncMessage);
            return new(p.Name, synced, syncDetail, SyncMessage);
        }

        var auto = string.IsNullOrWhiteSpace(userMessage);
        var message = auto ? status.AutoMessage() : userMessage!.Trim();
        var repoName = RepoName(p.Path);

        using var db = OpenDb();
        if (db is null)
            return new(p.Name, false, $"no workflow database at {dbPath}; can't align the commit to a ticket", message);
        if (!db.Repos().Any(r => r.Name.Equals(repoName, StringComparison.OrdinalIgnoreCase)))
            return new(p.Name, false, $"{repoName} isn't in the workflow database; run 'workflow repos rollout tickets --apply'", message);

        var existing = TicketIds(message).FirstOrDefault(id => db.Ticket(id) is { } t && t.Repo.Equals(repoName, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            var (ok, detail, committed) = git.Commit(p.Path, message);
            return new(p.Name, ok, detail, committed, existing);
        }

        var ticket = db.Add(repoName, InferType(message, auto, status.Changes), TicketRules.Closed, Summary(message), "");
        db.Render(new Workspace(workspaceRoot), [repoName]);
        message = WithTicket(message, ticket.Id);

        var (done, result, commitMessage) = git.Commit(p.Path, message);
        if (!done && !result.StartsWith(GitService.PushFailedPrefix, StringComparison.Ordinal))
        {
            // Nothing was committed, so the closed ticket would claim work that never landed.
            db.Close(ticket.Id, TicketRules.WontDo, "commit failed");
            db.Render(new Workspace(workspaceRoot), [repoName]);
            result += $" ({ticket.Id} marked wont-do)";
        }
        return new(p.Name, done, result, commitMessage, ticket.Id, TicketCreated: true);
    }

    private ProjectCommitResult SyncWorkflowDb()
    {
        if (!Directory.Exists(WorkflowRepoPath)) return new(WorkflowRepoName, false, "repo not found; run 'workflow db sync' to push the new tickets");
        WriteDump();
        var (ok, detail) = git.CommitPaths(WorkflowRepoPath, SyncMessage,
            [WorkflowDb.RelativePath, WorkflowDb.DumpRelativePath]);
        var landed = ok || detail.StartsWith(GitService.PushFailedPrefix, StringComparison.Ordinal);
        return new(WorkflowRepoName, ok, detail, landed ? SyncMessage : "");
    }

    /// <summary>The readable dump committed beside the database, the same file <c>workflow db sync</c> writes.</summary>
    private void WriteDump()
    {
        if (!Directory.Exists(WorkflowRepoPath)) return;
        using var db = OpenDb();
        if (db is null) return;
        var path = Path.Combine(WorkflowRepoPath, WorkflowDb.DumpRelativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, db.Dump(), new UTF8Encoding(false));
    }

    /// <summary>The database once it holds any repo; null before migration (same rule as <see cref="WorkflowDb.OpenIfMigrated"/>).</summary>
    private WorkflowDb? OpenDb()
    {
        if (!File.Exists(dbPath)) return null;
        var db = WorkflowDb.Open(dbPath);
        if (db.Repos().Count > 0) return db;
        db.Dispose();
        return null;
    }

    private bool IsWorkflowRepo(string path) =>
        string.Equals(Path.GetFullPath(path).TrimEnd('\\', '/'), Path.GetFullPath(WorkflowRepoPath).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

    private static bool IsDatabaseFile(string fileName) =>
        fileName.Equals(Path.GetFileName(WorkflowDb.RelativePath), StringComparison.OrdinalIgnoreCase)
        || fileName.Equals(Path.GetFileName(WorkflowDb.DumpRelativePath), StringComparison.OrdinalIgnoreCase);

    private static string RepoName(string path) => Path.GetFileName(Path.GetFullPath(path).TrimEnd('\\', '/'));

    [GeneratedRegex(@"\b(?:[A-Z]\.)?[A-Z][A-Z0-9]*-T\d+\b")]
    private static partial Regex TicketIdPattern();

    /// <summary>Ticket IDs (<c>CODE-T&lt;n&gt;</c>) named in a commit message, in order.</summary>
    public static IReadOnlyList<string> TicketIds(string message) =>
        [.. TicketIdPattern().Matches(message).Select(m => m.Value).Distinct(StringComparer.Ordinal)];

    /// <summary>
    /// The ticket type for a commit: docs when only documentation changed, otherwise read from the message's
    /// first word. An auto-generated message ("Add x.cs; Update y.cs") names files, not intent, so it's a chore.
    /// </summary>
    public static string InferType(string message, bool autoGenerated, IReadOnlyList<GitChange> changes)
    {
        if (changes.Count > 0 && changes.All(c => DocExtensions.Contains(Path.GetExtension(c.FileName)))) return "docs";
        if (autoGenerated) return "chore";
        var first = message.TrimStart().Split([' ', ':', '\t', '\r', '\n'], 2)[0].ToLowerInvariant();
        return first switch
        {
            "fix" or "fixes" or "fixed" or "repair" or "correct" or "bugfix" => "fix",
            "doc" or "docs" or "document" or "documentation" => "docs",
            "add" or "adds" or "implement" or "introduce" or "support" or "allow" or "let" or "enable" or "feat" or "feature" => "feature",
            _ => "chore",
        };
    }

    /// <summary>The message's first line, which becomes the ticket summary.</summary>
    public static string Summary(string message) => message.Trim().Split('\n')[0].Trim();

    /// <summary>Appends <c>(ID)</c> to the message's first line, the convention commit subjects already use.</summary>
    public static string WithTicket(string message, string id)
    {
        var trimmed = message.Trim();
        var nl = trimmed.IndexOf('\n');
        return nl < 0 ? $"{trimmed} ({id})" : $"{trimmed[..nl].TrimEnd()} ({id}){trimmed[nl..]}";
    }
}
