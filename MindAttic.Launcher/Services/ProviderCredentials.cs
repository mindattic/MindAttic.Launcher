using System.Diagnostics;
using MindAttic.Vault.Credentials;

namespace MindAttic.Launcher.Services;

/// <summary>
/// Pushes each agent provider's API key — resolved from the shared MindAttic LLM
/// keyring (<see cref="LlmCredentialStore"/>, HOUSE-LAW-3; edited via
/// <see cref="LlmCredentials"/> and <see cref="Menus.SettingsMenu"/>) — to
/// wherever that provider's CLI expects to find it, right before it's
/// launched. Claude, Codex and Gemini read their key directly from a process
/// env var; Kimi only reads its own <c>config.toml</c>, so that file is
/// synced instead (see <see cref="KimiConfigSync"/>). A missing/blank Vault
/// entry is a no-op — the CLI falls back to however it's already configured
/// (its own login, an existing config.toml key, etc.), never a hard failure.
/// Claude is the one exception: a Vault key is never pushed alongside an
/// active claude.ai login (<see cref="ClaudeAuthStatus"/>) — the CLI's own
/// auth resolution gets unpredictable with both set, and claude.ai wins
/// anyway, so there is nothing to gain by setting it.
/// </summary>
public static class ProviderCredentials
{
    /// <summary>Provider keys whose CLI reads its API key directly from this env var.</summary>
    private static readonly IReadOnlyDictionary<string, string> EnvVarByProviderKey =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Claude"] = "ANTHROPIC_API_KEY",
            ["Codex"] = "OPENAI_API_KEY",
            ["Gemini"] = "GEMINI_API_KEY",
        };

    /// <summary>
    /// Resolves <paramref name="providerKey"/>'s Vault entry and applies it —
    /// as an env var on <paramref name="psi"/>, or via <see cref="KimiConfigSync"/>
    /// for Kimi. <paramref name="store"/> defaults to the real shared keyring;
    /// <paramref name="kimiConfigPath"/> defaults to the real
    /// <c>~/.kimi-code/config.toml</c>; <paramref name="claudeCredentialsPath"/>
    /// defaults to the real <c>~/.claude/.credentials.json</c>. Tests pass all
    /// three overrides so a run never touches real Vault data, the user's
    /// actual Kimi config, or their actual claude.ai login state.
    /// </summary>
    public static void Apply(
        ProcessStartInfo psi,
        string providerKey,
        ICredentialStore? store = null,
        string? kimiConfigPath = null,
        string? claudeCredentialsPath = null)
    {
        if (string.Equals(providerKey, "Claude", StringComparison.OrdinalIgnoreCase)
            && ClaudeAuthStatus.IsLoggedIntoClaudeAi(claudeCredentialsPath))
            return;

        var key = (store ?? LlmCredentialStore.Default).GetKey(providerKey);
        if (string.IsNullOrWhiteSpace(key)) return;

        if (EnvVarByProviderKey.TryGetValue(providerKey, out var envVar))
        {
            psi.Environment[envVar] = key;
            return;
        }

        if (string.Equals(providerKey, "Kimi", StringComparison.OrdinalIgnoreCase))
            KimiConfigSync.EnsureApiKey(kimiConfigPath ?? KimiConfigSync.DefaultConfigPath, KimiConfigSync.ProviderTable, key);
    }
}
