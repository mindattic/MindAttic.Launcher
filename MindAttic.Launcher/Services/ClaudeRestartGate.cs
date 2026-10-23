using System.Text.Json;

namespace MindAttic.Launcher.Services;

/// <summary>
/// When a hosted Claude CLI may be restarted for a <see cref="RestartResumeRequest"/>: only the CLI the
/// request names (<see cref="IsFor"/>), only at its next idle point — its turn has ended
/// (<c>~/.claude/session-context/&lt;session&gt;.state.json</c> says <c>"state": "idle"</c>, as the
/// Orchestrator's context gate writes it) and no subagent of it runs (no mark in
/// <c>session-context/subagents</c> holding its pid) — and only while the billing mode is still the one
/// asked for (<see cref="StillWanted"/>). Never mid-turn: a restart then would lose the turn's work.
/// </summary>
public static class ClaudeRestartGate
{
    /// <summary>A subagent mark older than this is a leftover of a crashed CLI or subagent, not a live one.</summary>
    public static readonly TimeSpan StaleSubagentMark = TimeSpan.FromHours(2);

    public static bool IsFor(RestartResumeRequest request, int runningCliPid) => request.CliPid == runningCliPid;

    /// <summary>A request that only moves the login (no billing) is always still wanted, and so is a failover: its
    /// billing is the CLI's own, not the mode's (M.LNCH-T67).</summary>
    public static bool StillWanted(RestartResumeRequest request, string? billingModePath = null) =>
        request.Billing is null || request.Failover || ClaudeBillingMode.Read(billingModePath) == request.Billing;

    /// <summary>Whether the CLI may be restarted for <paramref name="request"/> now: at once for <c>"now":true</c> (its
    /// turn already ended at a limit error), else only at its idle point (<see cref="IsIdle"/>).</summary>
    public static bool IsReady(RestartResumeRequest request, string? sessionContextDirectory = null, DateTime? nowUtc = null) =>
        request.Now || IsIdle(request, sessionContextDirectory, nowUtc);

    public static bool IsIdle(RestartResumeRequest request, string? sessionContextDirectory = null, DateTime? nowUtc = null)
    {
        var dir = sessionContextDirectory ?? ClaudeBillingMode.SessionContextDirectory;
        if (request.Session.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return false;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, request.Session + ".state.json")));
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("state", out var state) || state.ValueKind != JsonValueKind.String ||
                state.GetString() != "idle")
                return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
        return !HasRunningSubagent(dir, request.CliPid, nowUtc ?? DateTime.UtcNow);
    }

    private static bool HasRunningSubagent(string dir, int cliPid, DateTime nowUtc)
    {
        var marks = Path.Combine(dir, "subagents");
        if (!Directory.Exists(marks)) return false;
        var pid = cliPid.ToString(System.Globalization.CultureInfo.InvariantCulture);
        foreach (var mark in Directory.EnumerateFiles(marks))
        {
            try
            {
                if (nowUtc - File.GetLastWriteTimeUtc(mark) > StaleSubagentMark) continue;
                if (File.ReadAllText(mark).Trim() == pid) return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A mark being written or removed right now: count it as running, so the restart waits a moment.
                return true;
            }
        }
        return false;
    }
}
