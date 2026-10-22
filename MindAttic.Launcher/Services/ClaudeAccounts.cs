using System.Text.Json;

namespace MindAttic.Launcher.Services;

/// <summary>
/// One Claude login a tab could run under (M.LNCH-T53): the global <c>~/.claude</c> login, or a per-CLI login
/// under <c>~/.claude-accounts/&lt;email&gt;</c>. <see cref="ConfigDir"/> is exactly what
/// <see cref="ClaudeLogin.Apply"/> expects ("" for the global login). <see cref="HasUsageData"/>,
/// <see cref="FiveHourLeftPercent"/> and <see cref="WeekLeftPercent"/> are best-effort: any of them can be
/// unknown when the Orchestrator isn't installed or hasn't read this account's meter yet.
/// </summary>
public sealed record ClaudeAccountOption(
    string Email,
    string ConfigDir,
    bool IsGlobal,
    double? FiveHourLeftPercent,
    double? WeekLeftPercent,
    bool HasUsageData,
    bool HasCli,
    bool IsAutoPick)
{
    /// <summary>A short label for a prompt or a tab title: the email, or "the global login" (with its own
    /// email in parentheses when known) for the global login — echoes <see cref="ClaudeLogin.Describe"/>'s wording.</summary>
    public string Describe() => IsGlobal
        ? (string.IsNullOrWhiteSpace(Email) ? "the global login" : $"the global login ({Email})")
        : Email;
}

/// <summary>
/// The Claude logins signed in on this machine (M.LNCH-T53): the global <c>~/.claude</c> login plus every
/// <c>~/.claude-accounts/&lt;email&gt;</c> per-CLI login, cross-referenced against
/// <c>orchestrator accounts --json</c> for usage and <c>orchestrator accounts pick --json</c> for the Auto
/// choice (both via <see cref="OrchestratorCli"/>). Both calls are best-effort: MindAttic.Orchestrator may not
/// be installed, may error, or may simply not have read a given account's meter yet — any of that degrades to
/// "not read" rather than failing the catalog, the same tolerance M.LNCH-T51 asked for ("no change when the
/// orchestrator isn't installed").
/// </summary>
public static class ClaudeAccountCatalog
{
    /// <summary>Test seam for the two orchestrator calls this makes; production uses <see cref="OrchestratorCli.Run"/>.
    /// The <c>string[]</c> is the args (e.g. <c>["accounts","--json"]</c>); return null for "orchestrator has nothing to say".</summary>
    public static Func<string[], string?>? OrchestratorOverride { get; set; }

    /// <summary>Test seam for the global login's email (normally read from <c>~/.claude.json</c>'s
    /// <c>oauthAccount.emailAddress</c>, the same field MindAttic.Orchestrator's <c>AccountProfile</c> reads).</summary>
    public static Func<string?>? GlobalEmailOverride { get; set; }

    /// <summary>Test seam for "does this configDir already have a CLI running right now"; production uses
    /// <see cref="ClaudeLoginMarker.IsBusy"/>.</summary>
    public static Func<string, bool>? BusyOverride { get; set; }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static string DefaultAccountsRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude-accounts");

    /// <summary>Per-CLI login folder names under <paramref name="accountsRoot"/> (each an email), sorted;
    /// empty when the folder doesn't exist — i.e. no per-CLI login has ever been added on this machine.</summary>
    public static IReadOnlyList<string> PerCliLogins(string? accountsRoot = null)
    {
        var root = accountsRoot ?? DefaultAccountsRoot;
        if (!Directory.Exists(root)) return Array.Empty<string>();
        return Directory.EnumerateDirectories(root)
            .Select(Path.GetFileName)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n!)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Where <paramref name="email"/>'s per-CLI login folder is, mirroring the folder-path shape
    /// <see cref="ClaudeLogin.Describe"/> already expects.</summary>
    public static string ConfigDirFor(string email, string? accountsRoot = null) =>
        Path.Combine(accountsRoot ?? DefaultAccountsRoot, email);

    /// <summary>
    /// Every signed-in login — the global one first, then each per-CLI login by email — with usage and
    /// busy-ness filled in where known, and exactly one flagged <see cref="ClaudeAccountOption.IsAutoPick"/>
    /// (the Orchestrator's pick, or the global login when the Orchestrator has nothing to say).
    /// </summary>
    public static IReadOnlyList<ClaudeAccountOption> List(DateTime nowUtc, string? accountsRoot = null)
    {
        var root = accountsRoot ?? DefaultAccountsRoot;
        var roster = TryRoster();
        var globalEmail = GlobalEmailOverride is not null ? GlobalEmailOverride() : ReadGlobalEmail();

        var options = new List<ClaudeAccountOption> { Build("", globalEmail ?? "", isGlobal: true, roster, nowUtc) };
        foreach (var email in PerCliLogins(root))
            options.Add(Build(ConfigDirFor(email, root), email, isGlobal: false, roster, nowUtc));

        var autoConfigDir = Normalize(TryAutoPick()?.ConfigDir);
        var matched = false;
        for (var i = 0; i < options.Count; i++)
        {
            if (matched || !string.Equals(Normalize(options[i].ConfigDir), autoConfigDir, StringComparison.OrdinalIgnoreCase)) continue;
            options[i] = options[i] with { IsAutoPick = true };
            matched = true;
        }
        // No pick (Auto off, or the Orchestrator isn't installed/errored): default the preselection to the
        // global login rather than leaving nothing marked.
        if (!matched) options[0] = options[0] with { IsAutoPick = true };
        return options;
    }

    private static ClaudeAccountOption Build(string configDir, string email, bool isGlobal, IReadOnlyList<OrchestratorAccountRow>? roster, DateTime nowUtc)
    {
        var row = string.IsNullOrEmpty(email) ? null : roster?.FirstOrDefault(r => string.Equals(r.Email, email, StringComparison.OrdinalIgnoreCase));
        var busy = BusyOverride?.Invoke(configDir) ?? ClaudeLoginMarker.IsBusy(configDir);
        return new ClaudeAccountOption(
            email, configDir, isGlobal,
            FiveHourLeftPercent: row is null ? null : LeftPercent(row.FiveHourPercent, row.FiveHourResetUtc, nowUtc),
            WeekLeftPercent: row is null ? null : LeftPercent(row.WeekPercent, row.WeekResetUtc, nowUtc),
            HasUsageData: row is not null,
            HasCli: busy,
            IsAutoPick: false);
    }

    /// <summary>The percent of a window left now: 0% used (100% left) once its reset has passed, null when
    /// the window was never read — mirrors MindAttic.Orchestrator's own <c>AccountRow.WeekLeftAt</c>/
    /// <c>FiveHourUsedAt</c> math, recomputed here since this side only has the raw JSON, not those types.</summary>
    private static double? LeftPercent(double? percent, DateTime? resetUtc, DateTime nowUtc)
    {
        var used = resetUtc is { } r && r <= nowUtc ? 0 : percent;
        return used is { } u ? Math.Max(0, 100 - u) : null;
    }

    private static string Normalize(string? configDir) => (configDir ?? "").Trim();

    private static string? ReadGlobalEmail()
    {
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude.json");
            if (!File.Exists(path)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            return doc.RootElement.TryGetProperty("oauthAccount", out var oauth) && oauth.ValueKind == JsonValueKind.Object &&
                   oauth.TryGetProperty("emailAddress", out var e) && e.ValueKind == JsonValueKind.String
                ? e.GetString()
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static IReadOnlyList<OrchestratorAccountRow>? TryRoster()
    {
        var stdout = Run("accounts", "--json");
        if (string.IsNullOrWhiteSpace(stdout)) return null;
        try { return JsonSerializer.Deserialize<List<OrchestratorAccountRow>>(stdout, JsonOptions); }
        catch (JsonException) { return null; }
    }

    private static OrchestratorPick? TryAutoPick()
    {
        var stdout = Run("accounts", "pick", "--json");
        if (string.IsNullOrWhiteSpace(stdout)) return null;
        try { return JsonSerializer.Deserialize<OrchestratorPick>(stdout, JsonOptions); }
        catch (JsonException) { return null; }
    }

    private static string? Run(params string[] args) =>
        OrchestratorOverride is not null ? OrchestratorOverride(args) : OrchestratorCli.Run(args);

    /// <summary>Only the fields this side reads from an <c>AccountRow</c> (MindAttic.Orchestrator.Core); extra
    /// JSON members (Account, Org, Plan, ReadUtc, SignedInUtc, Label) are ignored by the deserializer.</summary>
    private sealed record OrchestratorAccountRow(string? Email, double? WeekPercent, DateTime? WeekResetUtc, double? FiveHourPercent, DateTime? FiveHourResetUtc);

    /// <summary>Shape of <c>orchestrator accounts pick --json</c>'s one line.</summary>
    private sealed record OrchestratorPick(string? ConfigDir, string? Email, string? Reason);
}
