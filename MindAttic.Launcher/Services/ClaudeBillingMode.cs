using System.Text.Json;

namespace MindAttic.Launcher.Services;

/// <summary>
/// What every Claude CLI bills, as MindAttic.Orchestrator's billing switch (M.ORCH-T436) last set it:
/// <c>team</c> (the claude.ai Claude Team login) or <c>api</c> (the Vault's <c>Claude</c> key), read from
/// <c>~/.claude/session-context/billing.json</c>. No file, an unreadable one or another mode reads as null,
/// which keeps the old rule (no key while a claude.ai login is signed in). The contract is the
/// Orchestrator's docs/ORCHESTRATION.md, section Billing.
/// </summary>
public static class ClaudeBillingMode
{
    public const string Team = "team";
    public const string Api = "api";

    public static string SessionContextDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "session-context");

    public static string DefaultPath => Path.Combine(SessionContextDirectory, "billing.json");

    /// <summary>
    /// The billing a hosted CLI keeps after <paramref name="request"/> (M.LNCH-T67), on top of the mode: a failover's
    /// <c>billing</c> is the CLI's own and holds for its later restarts; any other request with a <c>billing</c> puts it
    /// back on the mode (null); a request without one (a login move) keeps <paramref name="current"/>.
    /// </summary>
    public static string? Next(string? current, RestartResumeRequest request) =>
        request.Failover ? request.Billing : request.Billing is null ? current : null;

    /// <summary>
    /// The billing a login folder carries itself (M.ORCH-T906): <see cref="Api"/> when its <c>mindattic-account.json</c> says
    /// <c>"billing": "api"</c>, which is how MindAttic.Orchestrator marks the Vault API key's own identity (a login folder with no
    /// claude.ai sign-in, whose every CLI bills the key); null for any other folder, the global login, or one that can't be read.
    /// </summary>
    public static string? OfLogin(string? configDir)
    {
        if (string.IsNullOrWhiteSpace(configDir)) return null;
        try
        {
            var path = Path.Combine(configDir, AccountScope.LookFileName);
            if (!File.Exists(path)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            return doc.RootElement.ValueKind == JsonValueKind.Object &&
                   doc.RootElement.TryGetProperty("billing", out var b) && b.ValueKind == JsonValueKind.String &&
                   string.Equals(b.GetString()?.Trim(), Api, StringComparison.OrdinalIgnoreCase)
                ? Api : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary><see cref="Team"/>, <see cref="Api"/>, or null when no mode is set.</summary>
    public static string? Read(string? path = null)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path ?? DefaultPath));
            if (doc.RootElement.ValueKind != JsonValueKind.Object ||
                !doc.RootElement.TryGetProperty("mode", out var mode) || mode.ValueKind != JsonValueKind.String)
                return null;
            return mode.GetString()?.Trim().ToLowerInvariant() switch
            {
                Team => Team,
                Api => Api,
                _ => null,
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }
}
