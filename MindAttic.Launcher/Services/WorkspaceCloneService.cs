namespace MindAttic.Launcher.Services;

public sealed record CloneResult(string RepoName, bool Ok, bool AlreadyPresent, string? Message);

/// <summary>
/// Clones every repo tagged with <see cref="TopicRepoSync.Topic"/> that isn't already present
/// under the workspace root, for <c>bootstrap</c>. Reuses the exact same GitHub-topic discovery
/// <see cref="TopicRepoSync"/>/<see cref="GitHubTopicRepos"/> already use for roster filtering,
/// rather than a hand-maintained repo list — Bob's own README calls its <c>$Repos</c> array
/// "update whenever a new repo is added" toil; tagging/untagging <c>wip</c> on GitHub is the only
/// lever needed here, same as the existing Discovery flow.
/// </summary>
public sealed class WorkspaceCloneService(GitService git)
{
    public WorkspaceCloneService() : this(new GitService()) { }

    /// <summary>The repo leaf name (and clone target directory) from a lower-cased <c>owner/repo</c> full name.</summary>
    public static string DeriveRepoName(string fullName)
    {
        var slash = fullName.LastIndexOf('/');
        return slash >= 0 ? fullName[(slash + 1)..] : fullName;
    }

    public static string DeriveCloneUrl(string fullName) => $"https://github.com/{fullName}.git";

    /// <summary>
    /// Clones every tagged repo not already present at <c>{workspaceRoot}\{name}</c>. One bad
    /// clone is reported and skipped rather than aborting the rest — same posture as every other
    /// per-item operation in this app (commit, backup, restore).
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
            if (Directory.Exists(path))
            {
                result = new CloneResult(name, Ok: true, AlreadyPresent: true, "already present");
            }
            else
            {
                var (ok, message) = git.Clone(workspaceRoot, DeriveCloneUrl(fullName), name);
                result = new CloneResult(name, ok, AlreadyPresent: false, message);
            }

            results.Add(result);
            onDone?.Invoke(result);
        }
        return results;
    }
}
