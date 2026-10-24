using System.Text.Json;

namespace MindAttic.Launcher.Services;

/// <summary>One org member as <c>orchestrator members --json</c> reports it (M.LNCH-T71): only the fields this side
/// reads out of <c>MembersCommand</c>'s JSON (Role, JoinedUtc, UpdatedUtc and the "signed in" flags are ignored by
/// the deserializer).</summary>
public sealed record MemberOption(string Email, string? Name, string Role, string Tier, string Status)
{
    /// <summary>What a startup picker shows: the name (or the email when none is set) plus role and tier, or the
    /// name alone when there's no role/tier to show (WhoIsUsing's synthetic <see cref="WhoIsUsing.ApiKeyOption"/>
    /// entry, which is no one's role or tier).</summary>
    public string Describe()
    {
        var label = string.IsNullOrWhiteSpace(Name) ? Email : Name;
        return string.IsNullOrWhiteSpace(Role) && string.IsNullOrWhiteSpace(Tier) ? label : $"{label} — {Role}, {Tier}";
    }
}

/// <summary>
/// The organization's current members and their eligibility (M.LNCH-T71), read from <c>orchestrator members --json</c> —
/// the same <c>orchestrator</c> CLI <see cref="ClaudeAccountCatalog"/> already shells out to, backed by whichever store
/// MindAttic.Orchestrator is configured for (SQLite or the full SQL Server instance, M.ORCH-T658). This replaces enumerating
/// local <c>~/.claude-accounts</c> folders (<see cref="ClaudeAccountCatalog.PerCliLogins"/>) as the source for "who may use
/// this Launcher": a folder only shows who has ever signed in on this machine, not who is an active member of the org right
/// now, so it drifts as people join, leave or go on hold. <c>orchestrator</c> not being on PATH, erroring, timing out or
/// returning nothing is not a Launcher failure — it degrades to "no roster", the same tolerance M.LNCH-T51 asked for.
/// </summary>
public static class OrchestratorMembers
{
    /// <summary>Test seam for the one orchestrator call this makes; production uses <see cref="OrchestratorCli.Run"/>.</summary>
    public static Func<string[], string?>? OrchestratorOverride { get; set; }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Every active member (<see cref="MemberRosterStatuses.Active"/>), by name then email; null when the
    /// orchestrator has nothing to say (not installed, errored, or no members configured yet).</summary>
    public static IReadOnlyList<MemberOption>? ActiveRoster()
    {
        var stdout = Run("members", "--json");
        if (string.IsNullOrWhiteSpace(stdout)) return null;
        List<MemberOption>? all;
        try { all = JsonSerializer.Deserialize<List<MemberOption>>(stdout, JsonOptions); }
        catch (JsonException) { return null; }
        if (all is null) return null;

        var active = all
            .Where(m => string.Equals(m.Status, MemberRosterStatuses.Active, StringComparison.OrdinalIgnoreCase))
            .OrderBy(m => m.Name ?? m.Email, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return active.Count == 0 ? null : active;
    }

    private static string? Run(params string[] args) =>
        OrchestratorOverride is not null ? OrchestratorOverride(args) : OrchestratorCli.Run(args);
}

/// <summary>Mirrors MindAttic.Orchestrator's <c>MemberRoster.Statuses</c> ("active", "pending") without a project
/// reference back to it; only the value this side branches on is named here.</summary>
public static class MemberRosterStatuses
{
    public const string Active = "active";
}
