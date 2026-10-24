namespace MindAttic.Launcher.Services;

/// <summary>
/// The interactive "which Claude account?" prompt (M.LNCH-T53), shown inside a freshly opened Claude tab
/// before its CLI starts. Plain <see cref="TextReader"/>/<see cref="TextWriter"/> rather than the Spectre
/// live-region <c>Ui.Menu</c> widget — this runs at the very start of a brand-new console host (no header,
/// no prior layout to preserve), and a numbered list typed by hand is enough for a one-off per-tab choice.
/// </summary>
public static class ClaudeAccountPrompt
{
    /// <summary>Lists <paramref name="options"/>, each with its usage and busy-ness, <paramref name="auto"/>
    /// marked and preselected by default (Enter alone accepts it), and returns whichever the user typed — or
    /// <paramref name="auto"/> for a blank line or anything that doesn't parse to a listed number.</summary>
    public static ClaudeAccountOption? Ask(
        IReadOnlyList<ClaudeAccountOption> options, ClaudeAccountOption auto,
        TextReader? input = null, TextWriter? output = null)
    {
        var inp = input ?? System.Console.In;
        var outp = output ?? System.Console.Out;
        var autoIndex = Math.Max(0, options.ToList().FindIndex(o => o == auto));

        outp.WriteLine("Choose a Claude account for this tab:");
        for (var i = 0; i < options.Count; i++)
            outp.WriteLine($"  {i + 1}) {options[i].Describe()} — {Details(options[i], i == autoIndex)}");
        outp.Write($"Account [{autoIndex + 1}]: ");

        var line = inp.ReadLine();
        if (string.IsNullOrWhiteSpace(line)) return auto;
        return int.TryParse(line.Trim(), out var n) && n >= 1 && n <= options.Count ? options[n - 1] : auto;
    }

    /// <summary>What the question says about <paramref name="o"/> after its name: its usage left, whether it already has a
    /// CLI and whether it's the Auto pick.</summary>
    public static string Details(ClaudeAccountOption o, bool isAuto)
    {
        var usage = o.HasUsageData
            ? $"5h left {Pct(o.FiveHourLeftPercent)}, week left {Pct(o.WeekLeftPercent)}"
            : "usage not read yet";
        var busy = o.HasCli ? ", already has a CLI" : "";
        var mark = isAuto ? " (Auto)" : "";
        return $"{usage}{busy}{mark}";
    }

    private static string Pct(double? p) => p is { } v ? $"{v:0}%" : "—";
}
