namespace MindAttic.Launcher.Services;

/// <summary>
/// Resolves who is using this Launcher (M.LNCH-T71) once, at startup, when it wasn't told outright
/// (<c>--account</c>, <see cref="Commands.MainMenuCommand"/>): from <see cref="OrchestratorMembers.ActiveRoster"/>,
/// never asking when there's no real choice (no roster, or exactly one active member), and otherwise asking once
/// here rather than letting every opened tab ask again (the per-tab screen this replaced, M.LNCH-T65).
/// </summary>
public static class WhoIsUsing
{
    /// <summary>Test seam for the interactive question; production uses <see cref="Prompt.Ask"/>.</summary>
    public static Func<IReadOnlyList<MemberOption>, MemberOption?>? AskOverride { get; set; }

    /// <summary>Test seam for the roster; production uses <see cref="OrchestratorMembers.ActiveRoster"/>.</summary>
    public static Func<IReadOnlyList<MemberOption>?>? RosterOverride { get; set; }

    /// <summary>The chosen member's email, or null when nobody should be asked (no roster, orchestrator not
    /// installed) or the one person asked backed out.</summary>
    public static string? Resolve()
    {
        var roster = RosterOverride is not null ? RosterOverride() : OrchestratorMembers.ActiveRoster();
        if (roster is null or { Count: 0 }) return null;
        if (roster.Count == 1) return roster[0].Email;

        var chosen = AskOverride is not null ? AskOverride(roster) : Prompt.Ask(roster);
        return chosen?.Email;
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
