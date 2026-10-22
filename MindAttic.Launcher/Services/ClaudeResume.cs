namespace MindAttic.Launcher.Services;

/// <summary>
/// <c>host --resume &lt;session&gt;</c> (M.LNCH-T64): a Claude tab that starts by resuming a given conversation
/// (<c>claude --resume &lt;id&gt;</c>) instead of a fresh one, so a pane MindAttic.Orchestrator reopens after a
/// dashboard restart runs through the host again and keeps its input pipe, control pipe, restart-resume and
/// account moves. A conversation lives under the login that wrote it (<c>&lt;configDir&gt;/projects/&lt;cwd&gt;/&lt;id&gt;.jsonl</c>),
/// so when the caller names no login the host resumes it under the one whose folder holds it.
/// </summary>
public static class ClaudeResume
{
    /// <summary>The error for <c>--resume</c> on <paramref name="providerKey"/>, or null when it's allowed (Claude only:
    /// the other CLIs resume with their own syntax, and only Claude tabs have the control pipe this exists to keep).</summary>
    public static string? Validate(string providerKey, string? session)
    {
        if (session is null) return null;
        if (string.IsNullOrWhiteSpace(session)) return "--resume needs a session id.";
        if (!string.Equals(providerKey, "Claude", StringComparison.OrdinalIgnoreCase))
            return $"--resume is only supported for Claude tabs, not {providerKey}.";
        if (session.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || session.Contains("..", StringComparison.Ordinal))
            return $"Not a session id: {session}";
        return null;
    }

    /// <summary>Where the global login keeps its data (<c>~/.claude</c>), i.e. what <c>""</c> means as a config dir.</summary>
    public static string GlobalConfigDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");

    /// <summary>
    /// The login whose folder holds <paramref name="session"/>'s transcript: the first of <paramref name="candidates"/>
    /// (config dirs as <see cref="ClaudeLogin.Apply"/> takes them, "" = the global login) with a
    /// <c>projects/*/&lt;session&gt;.jsonl</c>. Null when none does, or a folder can't be read.
    /// </summary>
    public static string? FindConfigDir(string session, IEnumerable<string> candidates, string? globalConfigDir = null)
    {
        foreach (var candidate in candidates)
        {
            var root = string.IsNullOrWhiteSpace(candidate) ? globalConfigDir ?? GlobalConfigDir : candidate;
            var projects = Path.Combine(root, "projects");
            try
            {
                if (!Directory.Exists(projects)) continue;
                foreach (var dir in Directory.EnumerateDirectories(projects))
                {
                    if (File.Exists(Path.Combine(dir, session + ".jsonl")))
                        return candidate;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return null;
    }

    /// <summary>Every signed-in login to look a session up in: the global one, then each per-CLI login.</summary>
    public static IEnumerable<string> SignedInLogins(string? accountsRoot = null) =>
        new[] { "" }.Concat(ClaudeAccountCatalog.PerCliLogins(accountsRoot)
            .Select(email => ClaudeAccountCatalog.ConfigDirFor(email, accountsRoot)));
}
