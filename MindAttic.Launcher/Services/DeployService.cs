namespace MindAttic.Launcher.Services;

/// <summary>
/// Locates the sibling <c>MindAttic.Deploy</c> repo's published exe and
/// composes the command line for the non-interactive
/// <c>MindAttic.Deploy.exe all</c> sub-command (which itself iterates catalog
/// + site --all + app --all --include-disabled, tallying failures across
/// batches).
/// </summary>
public sealed class DeployService
{
    public const string SiblingRepoName = "MindAttic.Deploy";
    public const string ArtifactExeName = "MindAttic.Deploy.exe";

    private readonly Func<string, bool> exists;

    public DeployService() : this(File.Exists) { }

    /// <summary>Test seam — injectable file-existence predicate.</summary>
    public DeployService(Func<string, bool> exists)
    {
        this.exists = exists;
    }

    /// <summary>
    /// Returns the absolute path to the sibling MindAttic.Deploy exe, or
    /// <c>null</c> if the sibling repo / published artifact isn't present.
    /// </summary>
    public string? ResolveExe(string consoleRepoRoot)
    {
        if (string.IsNullOrWhiteSpace(consoleRepoRoot)) return null;
        var parent = Path.GetDirectoryName(consoleRepoRoot);
        if (string.IsNullOrEmpty(parent)) return null;

        var candidate = Path.Combine(parent, SiblingRepoName, "artifacts", ArtifactExeName);
        return exists(candidate) ? candidate : null;
    }

    /// <summary>
    /// Builds the cmd /k payload that invokes the Deploy CLI's "all"
    /// sub-command (which handles cross-batch sequencing + failure tallying
    /// internally).
    /// </summary>
    public static string BuildDeployAllCommandLine(string deployExe) =>
        $"\"{deployExe}\" all";
}
