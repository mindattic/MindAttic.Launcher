using System.Text.Json;

namespace MindAttic.Launcher.Services;

/// <summary>
/// Whether the <c>claude</c> CLI is currently logged in to claude.ai (a Pro/Max/Team
/// subscription) via its own OAuth flow, detected from a non-empty
/// <c>claudeAiOauth.accessToken</c> in <c>~/.claude/.credentials.json</c>.
/// </summary>
/// <remarks>
/// The CLI itself warns when both a claude.ai session and <c>ANTHROPIC_API_KEY</c>
/// are present — "auth may not work as expected" — and claude.ai wins. See
/// <see cref="ProviderCredentials.Apply"/>, which uses this to skip pushing a
/// Vault-stored Claude key on top of an active claude.ai login instead of
/// fighting the CLI's own precedence.
/// </remarks>
public static class ClaudeAuthStatus
{
    private static readonly string DefaultCredentialsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", ".credentials.json");

    /// <param name="credentialsPath">
    /// Overrides the real <c>~/.claude/.credentials.json</c> — tests pass a
    /// throwaway path so a run never reads the machine's actual login state.
    /// </param>
    public static bool IsLoggedIntoClaudeAi(string? credentialsPath = null)
    {
        var path = credentialsPath ?? DefaultCredentialsPath;
        if (!File.Exists(path)) return false;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            return doc.RootElement.TryGetProperty("claudeAiOauth", out var oauth)
                && oauth.TryGetProperty("accessToken", out var token)
                && !string.IsNullOrWhiteSpace(token.GetString());
        }
        catch
        {
            return false; // unreadable/malformed — behave as if not logged in
        }
    }
}
