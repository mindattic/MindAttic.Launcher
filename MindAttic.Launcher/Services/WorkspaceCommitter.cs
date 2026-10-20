using System.Text;
using System.Text.RegularExpressions;
using MindAttic.Launcher.Models;
using MindAttic.Tickets;
using MindAttic.Tickets.Data;
using MindAttic.Tickets.Log;

namespace MindAttic.Launcher.Services;

/// <summary>What happened to one repo during a commit run.</summary>
public sealed record ProjectCommitResult(
    string Project, bool Ok, string Detail, string CommitMessage = "", string? TicketId = null, bool TicketCreated = false);

/// <summary>
/// The one commit engine behind the Commit menu and <c>mindattic commit</c> (HOUSE-LAW-6). Every commit is
/// aligned to a MindAttic.Tickets ticket: a message naming one of the repo's tickets is kept as-is, otherwise a
/// closed ticket is created from the message and its ID appended, and the repo's generated USER_STORIES.md view
/// is rewritten before staging so the new row lands in the same commit. The MindAttic.Tickets repo, which
/// holds the database, is committed last so it carries every ticket the run created.
/// </summary>
public sealed partial class WorkspaceCommitter(GitService git, string workspaceRoot, string? dbPath = null)
{
    public const string TicketsRepoName = "MindAttic.Tickets";
    public const string SyncMessage = "Sync the ticket database";

    private static readonly HashSet<string> DocExtensions = new(StringComparer.OrdinalIgnoreCase) { ".md", ".htm", ".html", ".txt" };

    private readonly string dbPath = dbPath ?? TicketsDb.DefaultPath(workspaceRoot);

    private string TicketsRepoPath => Path.Combine(workspaceRoot, TicketsDb.HomeFolder(workspaceRoot));

    public IReadOnlyList<ProjectCommitResult> Run(IEnumerable<Project> projects, string? userMessage, Action<ProjectCommitResult>? report = null)
    {
        var results = new List<ProjectCommitResult>();
        void Add(ProjectCommitResult r) { results.Add(r); report?.Invoke(r); }

        var ordered = projects.OrderBy(p => IsTicketsRepo(p.Path) ? 1 : 0).ToList();
        string? lastCreated = null;
        var ticketsCommitted = false;
        foreach (var p in ordered)
        {
            var r = CommitProject(p, userMessage, lastCreated);
            if (r.TicketCreated) lastCreated = r.TicketId;
            ticketsCommitted |= IsTicketsRepo(p.Path);
            Add(r);
        }

        // A single-repo commit still created a ticket in the shared database; push it so other machines see it.
        if (lastCreated is not null && !ticketsCommitted) Add(SyncTicketsDb(lastCreated));
        return results;
    }

    /// <summary>The database sync's subject cites a ticket it carries, as the workspace commit-msg hook requires.</summary>
    public static string SyncMessageFor(string ticketId) => WithTicket(SyncMessage, ticketId);

    private ProjectCommitResult CommitProject(Project p, string? userMessage, string? carriedTicket)
    {
        if (!Directory.Exists(p.Path)) return new(p.Name, false, "path not found");

        var isTickets = IsTicketsRepo(p.Path);
        if (isTickets) WriteDump();

        var status = git.Status(p.Path);
        if (!status.IsValid) return new(p.Name, false, status.Error ?? "");
        if (status.IsClean)
        {
            // An earlier push failed (the remote moved ahead, or was offline): nothing new to commit, but still unsynced.
            var unpushed = git.UnpushedCount(p.Path);
            if (unpushed == 0) return new(p.Name, true, "nothing to commit");
            var (pushed, pushDetail) = git.PushPending(p.Path);
            return new(p.Name, pushed, pushed && pushDetail != GitService.ArchivedNotice ? $"pushed {unpushed} earlier commit(s)" : pushDetail);
        }

        if (isTickets && carriedTicket is not null && status.Changes.All(c => IsDatabaseFile(c.FileName)))
        {
            var (synced, syncDetail, syncMessage) = git.Commit(p.Path, SyncMessageFor(carriedTicket));
            return new(p.Name, synced, syncDetail, syncMessage, carriedTicket);
        }

        var auto = string.IsNullOrWhiteSpace(userMessage);
        var message = auto ? status.AutoMessage() : userMessage!.Trim();
        var repoName = RepoName(p.Path);

        using var db = OpenDb();
        if (db is null)
            return new(p.Name, false, $"no ticket database at {dbPath}; can't align the commit to a ticket", message);
        if (!db.Repos().Any(r => r.Name.Equals(repoName, StringComparison.OrdinalIgnoreCase)))
            return new(p.Name, false, $"{repoName} isn't in the ticket database; run 'tickets repos rollout tickets --apply'", message);

        var existing = TicketIds(message).FirstOrDefault(id => db.Ticket(id) is { } t && t.Repo.Equals(repoName, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            var (ok, detail, committed) = git.Commit(p.Path, message);
            return new(p.Name, ok, detail, committed, existing);
        }

        var ticket = db.Add(repoName, InferType(message, auto, status.Changes), TicketRules.Closed, Summary(message), "");
        db.Render(new Workspace(workspaceRoot), [repoName]);
        if (isTickets) WriteDump(); // the dump must carry this repo's own new ticket
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

    private ProjectCommitResult SyncTicketsDb(string ticketId)
    {
        if (!Directory.Exists(TicketsRepoPath)) return new(TicketsRepoName, false, "repo not found; run 'tickets db sync' to push the new tickets");
        WriteDump();
        var message = SyncMessageFor(ticketId);
        var (ok, detail) = git.CommitPaths(TicketsRepoPath, message,
            [TicketsDb.RelativePath, TicketsDb.DumpRelativePath]);
        var landed = ok || detail.StartsWith(GitService.PushFailedPrefix, StringComparison.Ordinal);
        return new(TicketsRepoName, ok, detail, landed ? message : "", ticketId);
    }

    /// <summary>The readable dump committed beside the database, the same file <c>tickets db sync</c> writes.</summary>
    private void WriteDump()
    {
        if (!Directory.Exists(TicketsRepoPath)) return;
        using var db = OpenDb();
        if (db is null) return;
        var path = Path.Combine(TicketsRepoPath, TicketsDb.DumpRelativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, db.Dump(), new UTF8Encoding(false));
    }

    /// <summary>The database once it holds any repo; null before migration (same rule as <see cref="TicketsDb.OpenIfMigrated"/>).</summary>
    private TicketsDb? OpenDb()
    {
        if (!File.Exists(dbPath)) return null;
        var db = TicketsDb.Open(dbPath);
        if (db.Repos().Count > 0) return db;
        db.Dispose();
        return null;
    }

    private bool IsTicketsRepo(string path) =>
        string.Equals(Path.GetFullPath(path).TrimEnd('\\', '/'), Path.GetFullPath(TicketsRepoPath).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

    private static bool IsDatabaseFile(string fileName) =>
        fileName.Equals(Path.GetFileName(TicketsDb.RelativePath), StringComparison.OrdinalIgnoreCase)
        || fileName.Equals(Path.GetFileName(TicketsDb.DumpRelativePath), StringComparison.OrdinalIgnoreCase);

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
