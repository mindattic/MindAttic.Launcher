using MindAttic.Vault.Credentials;

namespace MindAttic.Launcher.Services;

/// <summary>
/// Per-provider LLM API keys (Claude, Codex, Gemini, Kimi), resolved through
/// Vault's shared <see cref="LlmCredentialStore"/> (HOUSE-LAW-3) at
/// <c>%APPDATA%\MindAttic\LLM\providers.json</c> — the same keyring every
/// other MindAttic app checks before its own login/config, so a key set here
/// becomes the fallback everywhere, not just for this app's own hosted tabs
/// (see <see cref="ProviderCredentials"/> for how a hosted tab applies it).
/// </summary>
public static class LlmCredentials
{
    public static ICredentialStore DefaultStore => LlmCredentialStore.Default;

    public static string? GetKey(string providerId, ICredentialStore? store = null) =>
        (store ?? DefaultStore).GetKey(providerId);

    public static void SetKey(string providerId, string apiKey, ICredentialStore? store = null) =>
        (store ?? DefaultStore).SetKey(providerId, apiKey);
}
