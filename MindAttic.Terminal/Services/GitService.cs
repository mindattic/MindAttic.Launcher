using System.Diagnostics;

namespace MindAttic.Terminal.Services;

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
        if (!Directory.Exists(repoPath))                       return new GitStatus([], "PATH NOT FOUND");
        if (!Directory.Exists(Path.Combine(repoPath, ".git"))) return new GitStatus([], "not a git repo");

        var (code, stdout, stderr) = Run(repoPath, DefaultTimeout, "status", "--porcelain");
        if (code != 0) return new GitStatus([], $"git status failed: {stderr.Trim()}");
        return ParseStatus(stdout);
    }

    /// <summary>Convenience wrapper: returns the user-facing summary string (or the error explanation).</summary>
    public string ShortStatus(string repoPath) => Status(repoPath).Short();

    public (bool ok, string? message) Pull(string repoPath)
    {
        var (code, stdout, stderr) = Run(repoPath, DefaultTimeout, "pull", "--ff-only");
        if (code != 0) return (false, $"{stdout}\n{stderr}".Trim());
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
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = repoPath,
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
            return (124, "", $"git {string.Join(' ', args)} timed out after {timeout.TotalSeconds:0}s");
        }

        Task.WaitAll(stdoutTask, stderrTask);
        return (p.ExitCode, stdoutTask.Result, stderrTask.Result);
    }
}
