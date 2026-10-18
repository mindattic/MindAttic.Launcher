namespace MindAttic.Launcher.Services;

/// <summary>
/// One repo's outcome. <see cref="AlreadyPresent"/> means it was already on disk, so it was pulled
/// rather than cloned; <see cref="Message"/> then carries the pull's outcome.
/// </summary>
public sealed record CloneResult(string RepoName, bool Ok, bool AlreadyPresent, string? Message);

/// <summary>
/// Clones every repo tagged with <see cref="TopicRepoSync.Topic"/> that isn't already present
/// under the workspace root, and fast-forwards each one that is (Bob's <c>--unpack</c> pulled
/// existing repos too), for <c>bootstrap</c>. Reuses the exact same GitHub-topic discovery
/// <see cref="TopicRepoSync"/>/<see cref="GitHubTopicRepos"/> already use for roster filtering,
/// rather than a hand-maintained repo list — Bob's own README calls its <c>$Repos</c> array
/// "update whenever a new repo is added" toil; tagging/untagging <c>wip</c> on GitHub is the only
/// lever needed here, same as the existing Discovery flow.
/// </summary>
public sealed class WorkspaceCloneService
{
    public delegate (bool ok, string? message) CloneRunner(string workspaceRoot, string cloneUrl, string targetDirName);
    public delegate (bool ok, string? message) PullRunner(string repoPath);

    private readonly CloneRunner clone;
    private readonly PullRunner pull;

    public WorkspaceCloneService() : this(new GitService()) { }

    public WorkspaceCloneService(GitService git) : this(git.Clone, git.Pull) { }

    public WorkspaceCloneService(CloneRunner clone, PullRunner pull)
    {
        this.clone = clone;
        this.pull = pull;
    }

    /// <summary>The repo leaf name (and clone target directory) from a lower-cased <c>owner/repo</c> full name.</summary>
    public static string DeriveRepoName(string fullName)
    {
        var slash = fullName.LastIndexOf('/');
        return slash >= 0 ? fullName[(slash + 1)..] : fullName;
    }

    public static string DeriveCloneUrl(string fullName) => $"https://github.com/{fullName}.git";

    /// <summary>
    /// Clones every tagged repo not already present at <c>{workspaceRoot}\{name}</c> and pulls
    /// (<c>--ff-only</c>) every one that is. One bad clone or pull is reported and skipped rather
    /// than aborting the rest — same posture as every other per-item operation in this app
    /// (commit, backup, restore). A repo with no upstream to pull from is fine, not a failure; a
    /// non-empty folder that isn't a git repo is left alone and reported.
    /// </summary>
    public IReadOnlyList<CloneResult> CloneAll(
        string workspaceRoot, IReadOnlySet<string> taggedFullNames, Action<CloneResult>? onDone = null)
    {
        var results = new List<CloneResult>();
        foreach (var fullName in taggedFullNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
        {
            var name = DeriveRepoName(fullName);
            var path = Path.Combine(workspaceRoot, name);

            CloneResult result;
            if (IsGitRepo(path))
            {
                var (ok, message) = pull(path);
                result = ok || GitService.IsNoUpstream(message)
                    ? new CloneResult(name, Ok: true, AlreadyPresent: true, ok ? message : "no upstream to pull from")
                    : new CloneResult(name, Ok: false, AlreadyPresent: true, $"pull failed: {message}");
            }
            else if (Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any())
            {
                result = new CloneResult(name, Ok: false, AlreadyPresent: true,
                    "the folder exists but isn't a git repo, so it was left alone and not cloned");
            }
            else
            {
                var (ok, message) = clone(workspaceRoot, DeriveCloneUrl(fullName), name);
                result = new CloneResult(name, ok, AlreadyPresent: false, message);
            }

            results.Add(result);
            onDone?.Invoke(result);
        }
        return results;
    }

    /// <summary>A folder holding a <c>.git</c> directory, or a <c>.git</c> file (a worktree).</summary>
    private static bool IsGitRepo(string path)
    {
        var dotGit = Path.Combine(path, ".git");
        return Directory.Exists(dotGit) || File.Exists(dotGit);
    }
}
