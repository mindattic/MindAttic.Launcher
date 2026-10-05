namespace MindAttic.Launcher.Services;

/// <summary>
/// Parses a git remote URL into a lower-cased GitHub <c>owner/repo</c> full
/// name, the same shape GitHub's API reports for a starred repo. Pure/static
/// so the starred-repo match logic is testable without a network call.
/// </summary>
public static class GitHubRepoRef
{
    private const string SshPrefix = "git@github.com:";

    /// <summary>
    /// Returns the lower-cased <c>owner/repo</c> for <paramref name="remoteUrl"/>,
    /// or <c>null</c> if it's blank, not a github.com remote, or doesn't carry
    /// exactly an owner and a repo segment.
    /// </summary>
    public static string? Parse(string? remoteUrl)
    {
        if (string.IsNullOrWhiteSpace(remoteUrl)) return null;
        var s = remoteUrl.Trim();
        if (s.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            s = s[..^".git".Length];

        if (s.StartsWith(SshPrefix, StringComparison.OrdinalIgnoreCase))
            return Normalize(s[SshPrefix.Length..]);

        if (Uri.TryCreate(s, UriKind.Absolute, out var uri) &&
            uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
            return Normalize(uri.AbsolutePath.Trim('/'));

        return null;
    }

    private static string? Normalize(string ownerSlashRepo)
    {
        var parts = ownerSlashRepo.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2 ? $"{parts[0]}/{parts[1]}".ToLowerInvariant() : null;
    }
}
