namespace MindAttic.Launcher.Services;

/// <summary>
/// Resolves who is using this Launcher (M.LNCH-T71) once, at startup, when it wasn't told outright
/// (<c>--account</c>, <see cref="Commands.MainMenuCommand"/>) — the Orchestrator always passes <c>--account</c>
/// itself, so reaching this at all means an unscoped launch: someone double-clicked the desktop shortcut, or ran
/// the exe bare in a terminal. From <see cref="OrchestratorMembers.ActiveRoster"/>, never asking when there's no
/// real choice at all (no roster) or when the login this process was explicitly started under (<c>CLAUDE_CONFIG_DIR</c>,
/// via <see cref="AccountScope.SignedInEmail"/>, the same <c>oauthAccount.emailAddress</c> Claude Code itself reports)
/// matches a roster member's email exactly (M.LNCH-T114): a person's name is not a reliable stand-in for which of
/// their Claude logins is live (e.g. a Team org seat vs. a personal plan under the same name), so the match goes by
/// the full sign-in email, never the display name. Deliberately does NOT trust the shared global <c>~/.claude</c>
/// login as that signal (M.LNCH-T118): with no <c>CLAUDE_CONFIG_DIR</c> to vouch for who this specific launch is for,
/// it falls back to asking rather than guessing from whoever last signed that shared file in. A single active member
/// is NOT skipped either (M.LNCH-T119): a desktop double-click has no login evidence of its own, so even the one
/// person on the roster still gets asked (and still sees the API Key option) rather than the Launcher silently
/// assuming it's them.
/// </summary>
public static class WhoIsUsing
{
    /// <summary>The sentinel <see cref="MemberOption.Email"/> for "bill the organization's API key, not a person's
    /// seat" (M.LNCH-T72; mirrors MindAttic.Orchestrator's <c>ClaudeAccount.ApiKey</c> = "api-key" without a project
    /// reference back to it, the same way <see cref="MemberRosterStatuses"/> mirrors its roster status). Resolve()
    /// appends it to the real roster whenever there's an actual choice to make. Picking it runs the Launcher
    /// unscoped (no login folder: <see cref="Commands.MainMenuCommand"/> sees a null account, same as nobody
    /// chosen) but first switches the dashboard's billing mode to api (<c>orchestrator billing api</c>, M.ORCH-T436),
    /// so every CLI this Launcher opens bills the Vault key instead of whichever login happens to be the default.</summary>
    public const string ApiKeyOption = "api-key";

    /// <summary>Test seam for the interactive question; production uses <see cref="Prompt.Ask"/>.</summary>
    public static Func<IReadOnlyList<MemberOption>, MemberOption?>? AskOverride { get; set; }

    /// <summary>Test seam for the roster; production uses <see cref="OrchestratorMembers.ActiveRoster"/>.</summary>
    public static Func<IReadOnlyList<MemberOption>?>? RosterOverride { get; set; }

    /// <summary>Test seam for the email signed in to the per-CLI login this process was explicitly started under;
    /// production reads <c>CLAUDE_CONFIG_DIR</c> and asks <see cref="AccountScope.SignedInEmail"/> of that folder,
    /// null when the variable isn't set (M.LNCH-T118: no fallback to the shared global login) or nobody is signed
    /// in there or it can't be read.</summary>
    public static Func<string?>? SignedInEmailOverride { get; set; }

    /// <summary>Test seam for switching billing when <see cref="ApiKeyOption"/> is chosen; production runs
    /// <c>orchestrator billing api</c> (best-effort: a failure here still leaves the dashboard's own toggle to fall
    /// back on, so it's never fatal to opening the Launcher).</summary>
    public static Action? SwitchToApiKeyOverride { get; set; }

    /// <summary>The chosen member's email, or null when nobody should be asked (no roster, orchestrator not
    /// installed), the one person asked backed out, or <see cref="ApiKeyOption"/> was chosen (the Launcher runs
    /// unscoped, billing already switched).</summary>
    public static string? Resolve()
    {
        var roster = RosterOverride is not null ? RosterOverride() : OrchestratorMembers.ActiveRoster();
        if (roster is null or { Count: 0 }) return null;

        var signedIn = SignedInEmailOverride is not null ? SignedInEmailOverride() : DetectSignedInEmail();
        if (signedIn is not null)
        {
            var match = roster.FirstOrDefault(m => string.Equals(m.Email, signedIn, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match.Email;
        }

        var choices = new List<MemberOption>(roster)
        {
            new(ApiKeyOption, "API Key — bill every CLI to the organization's Vault key, not a person's seat", "", "", ""),
        };
        var chosen = AskOverride is not null ? AskOverride(choices) : Prompt.Ask(choices);
        if (chosen is null) return null;
        if (chosen.Email != ApiKeyOption) return chosen.Email;

        if (SwitchToApiKeyOverride is not null) SwitchToApiKeyOverride();
        else try { OrchestratorCli.Run("billing", "api"); } catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException) { }
        return null;
    }

    /// <summary>The email signed in to the per-CLI login this process was explicitly started under
    /// (<c>CLAUDE_CONFIG_DIR</c>), or null when that variable isn't set (M.LNCH-T118).
    /// Deliberately does NOT fall back to the shared global <c>~/.claude</c> login: that one file is shared by every
    /// person on this Windows account and gets silently rewritten by whoever last signed in or refreshed a token
    /// there, regardless of who is asking "who's using this Launcher?" right now — trusting it as identity proof
    /// caused unscoped launches to get silently misattributed to a different roster member (M.LNCH-T118). Only a
    /// caller that already set <c>CLAUDE_CONFIG_DIR</c> for this specific process (a per-CLI login folder) is actual
    /// evidence of who this launch is for. Reads the same <c>.claude.json</c> <c>oauthAccount.emailAddress</c> field
    /// as <see cref="ClaudeAccountCatalog"/>.</summary>
    internal static string? DetectSignedInEmail()
    {
        var configDir = Environment.GetEnvironmentVariable(ClaudeLogin.ConfigDirVariable);
        return string.IsNullOrWhiteSpace(configDir) ? null : AccountScope.SignedInEmail(configDir);
    }

    /// <summary>The plain-console question itself, split out so <see cref="Resolve"/>'s skip rules stay testable
    /// without a console. Plain <see cref="TextReader"/>/<see cref="TextWriter"/>, like <see cref="ClaudeAccountPrompt"/>
    /// — this runs before the Launcher's Spectre console is set up for the session.</summary>
    public static class Prompt
    {
        public static MemberOption? Ask(IReadOnlyList<MemberOption> roster, TextReader? input = null, TextWriter? output = null)
        {
            var inp = input ?? System.Console.In;
            var outp = output ?? System.Console.Out;

            outp.WriteLine("Who's using MindAttic.Launcher?");
            for (var i = 0; i < roster.Count; i++)
                outp.WriteLine($"  {i + 1}) {roster[i].Describe()}");
            outp.Write("Choose a number: ");

            var line = inp.ReadLine();
            return int.TryParse(line?.Trim(), out var n) && n >= 1 && n <= roster.Count ? roster[n - 1] : null;
        }
    }
}
