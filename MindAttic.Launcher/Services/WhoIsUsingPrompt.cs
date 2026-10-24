namespace MindAttic.Launcher.Services;

/// <summary>
/// Resolves who is using this Launcher (M.LNCH-T71) once, at startup, when it wasn't told outright
/// (<c>--account</c>, <see cref="Commands.MainMenuCommand"/>): from <see cref="OrchestratorMembers.ActiveRoster"/>,
/// never asking when there's no real choice (no roster, or exactly one active member), and otherwise asking once
/// here rather than letting every opened tab ask again (the per-tab screen this replaced, M.LNCH-T65).
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
        if (roster.Count == 1) return roster[0].Email;

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
