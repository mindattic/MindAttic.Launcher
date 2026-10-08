using MindAttic.Vault.Credentials;

namespace MindAttic.Launcher.Services;

/// <summary>
/// The GitHub personal access token used to read the signed-in user's repos
/// and their topics, resolved through Vault's <see cref="TokenStore"/> (HOUSE-LAW-3)
/// rather than hard-coded or read straight from an environment variable.
/// Stored at <c>%APPDATA%\MindAttic\GitHub\tokens.json</c>.
/// </summary>
public static class GitHubCredentials
{
    public const string TokenName = "github";

    public static TokenStore DefaultStore { get; } = TokenStore.ForBucket("GitHub");

    public static string? GetToken(TokenStore? store = null) =>
        (store ?? DefaultStore).Get(TokenName);

    public static void SetToken(string token, TokenStore? store = null) =>
        (store ?? DefaultStore).Set(TokenName, token);
}
