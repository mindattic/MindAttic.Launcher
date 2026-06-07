using System.Diagnostics;
using MindAttic.Console.Models;

namespace MindAttic.Console.Services;

public enum ChangeKind { Added, Modified, Deleted }

public sealed record GitChange(ChangeKind Kind, string FileName);

/// <summary>
/// Snapshot of a repo's working tree. When <see cref="Error"/> is non-null,
/// the snapshot is invalid (path missing, not a git repo, command timed out)
/// and callers should treat the repo as "unknown" rather than "clean."
/// </summary>
public sealed record GitStatus(IReadOnlyList<GitChange> Changes, string? Error = null)
{
    public int Total => Changes.Count;
    public int Added    => Changes.Count(c => c.Kind == ChangeKind.Added);
    public int Modified => Changes.Count(c => c.Kind == ChangeKind.Modified);
    public int Deleted  => Changes.Count(c => c.Kind == ChangeKind.Deleted);
    public bool IsClean => Error is null && Changes.Count == 0;
    public bool IsValid => Error is null;

    public string Short()
    {
        if (Error is not null) return Error;
        if (Changes.Count == 0) return "clean";
        var parts = new List<string>();
        if (Added    > 0) parts.Add($"+{Added}");
        if (Modified > 0) parts.Add($"~{Modified}");
        if (Deleted  > 0) parts.Add($"-{Deleted}");
        return $"{Total} change(s) [{string.Join(' ', parts)}]";
    }

    public string AutoMessage(int maxLength = 200)
    {
        var added    = Changes.Where(c => c.Kind == ChangeKind.Added).Select(c => c.FileName).ToList();
        var modified = Changes.Where(c => c.Kind == ChangeKind.Modified).Select(c => c.FileName).ToList();
        var deleted  = Changes.Where(c => c.Kind == ChangeKind.Deleted).Select(c => c.FileName).ToList();

        var parts = new List<string>();
        if (added.Count    > 0) parts.Add($"Add {string.Join(", ", added)}");
        if (modified.Count > 0) parts.Add($"Update {string.Join(", ", modified)}");
        if (deleted.Count  > 0) parts.Add($"Remove {string.Join(", ", deleted)}");
        var msg = string.Join("; ", parts);

        if (msg.Length > maxLength)
        {
            var summary = new List<string>();
            if (added.Count    > 0) summary.Add($"Add {added.Count} file(s)");
            if (modified.Count > 0) summary.Add($"Update {modified.Count} file(s)");
            if (deleted.Count  > 0) summary.Add($"Remove {deleted.Count} file(s)");
            msg = string.Join("; ", summary);
        }

        return msg;
    }
}

/// <summary>
/// Git status parser + porcelain wrapper. The parser is pure so unit tests
/// don't need a real repo. Pull/Commit/Push shell out to git with a timeout
/// and <c>GIT_TERMINAL_PROMPT=0</c> so a missing credential cache fails fast
/// instead of hanging the tab.
/// </summary>
public sealed class GitService
{
    /// <summary>Default per-operation timeout. Long enough for a slow push, short enough that a hang surfaces quickly.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(120);

    public static GitStatus ParseStatus(string porcelain)
    {
        var changes = new List<GitChange>();
        if (string.IsNullOrEmpty(porcelain)) return new GitStatus(changes);

        foreach (var rawLine in porcelain.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.Length < 3) continue;

            var indexCode    = line[0];
            var worktreeCode = line[1];
            var path = line[3..];

            if (path.Contains("->"))
                path = path.Split("->").Last().Trim();
            path = path.Trim('"', ' ', '/');
            var name = path.Split('/', '\\').Last();

            var kind =
                (indexCode == '?' && worktreeCode == '?') ? ChangeKind.Added :
                (indexCode == 'A' || worktreeCode == 'A') ? ChangeKind.Added :
                (indexCode == 'D' || worktreeCode == 'D') ? ChangeKind.Deleted :
                                                            ChangeKind.Modified;

            changes.Add(new GitChange(kind, name));
        }

        return new GitStatus(changes);
    }

    public GitStatus Status(string repoPath)
    {
        if (!Directory.Exists(repoPath)) return new GitStatus([], "PATH NOT FOUND");
        // In a git worktree, .git is a *file* (gitdir: …), not a directory, so
        // check for either form before declaring "not a git repo".
        var dotGit = Path.Combine(repoPath, ".git");
        if (!Directory.Exists(dotGit) && !File.Exists(dotGit)) return new GitStatus([], "not a git repo");

        var (code, stdout, stderr) = Run(repoPath, DefaultTimeout, "status", "--porcelain");
        if (code != 0) return new GitStatus([], $"git status failed: {stderr.Trim()}");
        return ParseStatus(stdout);
    }

    /// <summary>Convenience wrapper: returns the user-facing summary string (or the error explanation).</summary>
    public string ShortStatus(string repoPath) => Status(repoPath).Short();

    /// <summary>
    /// Parallel fan-out of <see cref="ShortStatus"/> across a project roster.
    /// Returns a map keyed by project name. Per-project exceptions (git missing,
    /// permission errors) are caught and surfaced as the status string so one
    /// bad repo can't take down the menu.
    /// </summary>
    public Dictionary<string, string> FetchShortStatuses(IReadOnlyList<Project> projects)
    {
        var results = new Dictionary<string, string>();
        Parallel.ForEach(projects, p =>
        {
            string summary;
            try { summary = ShortStatus(p.Path); }
            catch (Exception ex) { summary = $"git error: {ex.Message}"; }
            lock (results) results[p.Name] = summary;
        });
        return results;
    }

    /// <summary>
    /// The repo's <c>origin</c> remote URL, or null when there's no origin, the
    /// path isn't a git repo, or git fails. Used to pre-fill the URL prompt when
    /// auto-discovering a project.
    /// </summary>
    public string? RemoteUrl(string repoPath)
    {
        if (!Directory.Exists(repoPath)) return null;
        var dotGit = Path.Combine(repoPath, ".git");
        if (!Directory.Exists(dotGit) && !File.Exists(dotGit)) return null;

        var (code, stdout, _) = Run(repoPath, DefaultTimeout, "remote", "get-url", "origin");
        if (code != 0) return null;
        var url = stdout.Trim();
        return string.IsNullOrWhiteSpace(url) ? null : url;
    }

    public (bool ok, string? message) Pull(string repoPath)
    {
        // Mirror Status()/Commit()'s pre-checks so the caller sees the same
        // "PATH NOT FOUND" / "not a git repo" wording instead of git's own
        // confusing fatal output.
        if (!Directory.Exists(repoPath)) return (false, "PATH NOT FOUND");
        var dotGit = Path.Combine(repoPath, ".git");
        if (!Directory.Exists(dotGit) && !File.Exists(dotGit)) return (false, "not a git repo");

        var (code, stdout, stderr) = Run(repoPath, DefaultTimeout, "pull", "--ff-only");
        if (code != 0)
        {
            // git usually explains itself on stdout/stderr, but a killed/odd run
            // can leave both blank — fall back to the exit code so the menu shows
            // a reason instead of a bare "pull failed".
            var detail = $"{stdout}\n{stderr}".Trim();
            return (false, detail.Length == 0 ? $"git pull failed (exit {code})" : detail);
        }
        return (true, stdout.Contains("Already up to date", StringComparison.OrdinalIgnoreCase)
            ? "up to date"
            : "updated");
    }

    public (bool ok, string message, string commitMessage) Commit(string repoPath, string? userMessage = null)
    {
        var status = Status(repoPath);
        if (!status.IsValid) return (false, status.Error!, "");
        if (status.IsClean)  return (true, "nothing to commit", "");

        var message = string.IsNullOrWhiteSpace(userMessage) ? status.AutoMessage() : userMessage.Trim();

        var (addCode, _, addErr) = Run(repoPath, DefaultTimeout, "add", "-A");
        if (addCode != 0) return (false, $"git add failed: {addErr.Trim()}", message);

        var (commitCode, _, commitErr) = Run(repoPath, DefaultTimeout, "commit", "-m", message);
        if (commitCode != 0) return (false, $"git commit failed: {commitErr.Trim()}", message);

        var (pushCode, _, pushErr) = Run(repoPath, DefaultTimeout, "push", "--quiet");
        if (pushCode != 0) return (false, $"git push failed: {pushErr.Trim()}", message);

        return (true, "synced", message);
    }

    private static (int code, string stdout, string stderr) Run(string repoPath, TimeSpan timeout, params string[] args)
    {
        // Scope git to the repo with -C only; we deliberately don't set
        // ProcessStartInfo.WorkingDirectory so there's one canonical path
        // resolution rule regardless of the caller's cwd.
        var psi = new ProcessStartInfo("git")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        // Refuse the credential prompt — if no cached creds exist we want git to
        // fail fast rather than wedge the tab waiting on stdin we never feed.
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";

        psi.ArgumentList.Add("-C");
        psi.ArgumentList.Add(repoPath);
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start git.");

        var stdoutTask = Task.Run(() => p.StandardOutput.ReadToEnd());
        var stderrTask = Task.Run(() => p.StandardError.ReadToEnd());

        if (!p.WaitForExit((int)timeout.TotalMilliseconds))
        {
            try { p.Kill(entireProcessTree: true); } catch { }
            // Drain the reader tasks so we don't leave them hanging on closed
            // streams. Killing the process closes stdout/stderr so the reads
            // should return promptly, but cap the wait defensively.
            try { Task.WaitAll(new[] { stdoutTask, stderrTask }, TimeSpan.FromSeconds(5)); } catch { }
            return (124, "", $"git {string.Join(' ', args)} timed out after {timeout.TotalSeconds:0}s");
        }

        // git itself has exited, but a process it spawned (a pager, a hook, a
        // credential helper) can inherit our redirected stdout/stderr and keep
        // the pipe open, leaving ReadToEnd blocked indefinitely. Cap the drain
        // the same way the timeout path does so one stuck grandchild can't wedge
        // the menu; read whatever finished.
        if (!Task.WaitAll(new[] { stdoutTask, stderrTask }, TimeSpan.FromSeconds(5)))
            return (p.ExitCode,
                stdoutTask.IsCompletedSuccessfully ? stdoutTask.Result : "",
                stderrTask.IsCompletedSuccessfully ? stderrTask.Result : "");
        return (p.ExitCode, stdoutTask.Result, stderrTask.Result);
    }
}
