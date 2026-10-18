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
