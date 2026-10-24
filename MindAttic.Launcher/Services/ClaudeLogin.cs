using System.Diagnostics;

namespace MindAttic.Launcher.Services;

/// <summary>
/// Which Claude login a hosted CLI runs under (M.LNCH-T52). Claude Code reads its login from
/// <c>CLAUDE_CONFIG_DIR</c> at process start: unset is the global login (<c>~/.claude</c>), a path is a
/// per-account login such as <c>%USERPROFILE%\.claude-accounts\&lt;email&gt;</c>. The host keeps its current
/// config dir and applies it to every start, so a restart-resume that moved the login keeps it on later
/// restarts.
/// </summary>
public static class ClaudeLogin
{
    public const string ConfigDirVariable = "CLAUDE_CONFIG_DIR";

    /// <summary>The login the host starts with: whatever CLAUDE_CONFIG_DIR it inherited ("" when none).</summary>
    public static string Initial(string? inherited) => inherited?.Trim() ?? "";

    /// <summary>The login a host's explicit <c>--config-dir</c> names: "" when it's the global login's own folder
    /// (<c>~/.claude</c>, how a scoped Launcher passes the global login), else the folder as given.</summary>
    public static string FromOption(string configDir, string? globalConfigDir = null)
    {
        var dir = configDir.Trim();
        var global = globalConfigDir ?? ClaudeResume.GlobalConfigDir;
        return string.Equals(Full(dir), Full(global), StringComparison.OrdinalIgnoreCase) ? "" : dir;

        static string Full(string path)
        {
            try { return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return path; }
        }
    }

    /// <summary>The login after <paramref name="request"/>: its <c>ConfigDir</c> when given, else <paramref name="current"/>.</summary>
    public static string Next(string current, RestartResumeRequest request) => request.ConfigDir ?? current;

    /// <summary>Sets CLAUDE_CONFIG_DIR on <paramref name="psi"/> to <paramref name="configDir"/>, or removes it for "" (the global login).</summary>
    public static void Apply(ProcessStartInfo psi, string configDir)
    {
        if (string.IsNullOrWhiteSpace(configDir))
            psi.EnvironmentVariables.Remove(ConfigDirVariable);
        else
            psi.EnvironmentVariables[ConfigDirVariable] = configDir;
    }

    /// <summary>A short name for <paramref name="configDir"/> to show in the tab: the folder name, or "the global login".</summary>
    public static string Describe(string configDir) =>
        string.IsNullOrWhiteSpace(configDir)
            ? "the global login"
            : Path.GetFileName(configDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) is { Length: > 0 } name
                ? name
                : configDir;
}
