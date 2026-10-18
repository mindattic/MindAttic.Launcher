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
/// Claude is the one exception: it follows the Orchestrator's billing mode
/// (<see cref="ClaudeBillingMode"/>). <c>team</c> never pushes the key and
/// strips an inherited one; <c>api</c> pushes it even with a claude.ai login
/// signed in (the switch has approved it in Claude Code). With no mode set, a
/// Vault key is never pushed alongside an active claude.ai login
/// (<see cref="ClaudeAuthStatus"/>) — the CLI's own auth resolution gets
/// unpredictable with both set, and claude.ai wins anyway.
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
    /// defaults to the real <c>~/.claude/.credentials.json</c>;
    /// <paramref name="billingModePath"/> defaults to the real
    /// <c>~/.claude/session-context/billing.json</c>. Tests pass the overrides
    /// so a run never touches real Vault data, the user's actual Kimi config,
    /// their actual claude.ai login state or their billing mode.
    /// </summary>
    public static void Apply(
        ProcessStartInfo psi,
        string providerKey,
        ICredentialStore? store = null,
        string? kimiConfigPath = null,
        string? claudeCredentialsPath = null,
        string? billingModePath = null)
    {
        if (string.Equals(providerKey, "Claude", StringComparison.OrdinalIgnoreCase))
        {
            var mode = ClaudeBillingMode.Read(billingModePath);
            if (mode == ClaudeBillingMode.Team)
            {
                // An inherited key (wt.exe's environment, a parent shell) would still bill it.
                psi.Environment.Remove(EnvVarByProviderKey["Claude"]);
                return;
            }
            if (mode is null && ClaudeAuthStatus.IsLoggedIntoClaudeAi(claudeCredentialsPath))
                return;
        }

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
