using Spectre.Console;

namespace MindAttic.Launcher.Ui;

/// <summary>
/// Header / footer helpers that match the original PS layout: breadcrumb
/// title across the top, dim key-hint footer across the bottom.
/// </summary>
public static class Screen
{
    // Everything drawn above the current menu since the last Header(), kept as
    // draw callbacks so Redraw() can repaint it at a new window size.
    private static readonly List<Action> HeaderParts = [];

    /// <summary>The alias of the login a scoped Launcher runs as (M.LNCH-T54), shown in every header; null unscoped.</summary>
    public static string? Scope { get; set; }

    public static void Header(params string[] breadcrumbs)
    {
        HeaderParts.Clear();
        AnsiConsole.Clear();
        Add(() =>
        {
            var root = Scope is { } alias ? $"MindAttic Launcher — {alias}" : "MindAttic Launcher";
            var trail = string.Join(" > ", new[] { root }.Concat(breadcrumbs));
            AnsiConsole.Write(new Rule($"[cyan1]{Markup.Escape(trail)}[/]")
            {
                Style = Theme.AccentStyle,
                Justification = Justify.Left
            });
            AnsiConsole.WriteLine();
        });
    }

    /// <summary>
    /// Draws <paramref name="draw"/> under the header now, and again whenever the
    /// screen is redrawn after a window resize. Use it for lines a menu shows above
    /// its list (status, notices) so a resize doesn't drop them.
    /// </summary>
    public static void Add(Action draw)
    {
        HeaderParts.Add(draw);
        draw();
    }

    /// <summary>
    /// Like <see cref="Add"/> for something already on screen (e.g. an answered
    /// text prompt): remembered for <see cref="Redraw"/> but not drawn again now.
    /// </summary>
    public static void Keep(Action draw) => HeaderParts.Add(draw);

    /// <summary>
    /// Clears the screen and repaints the header parts at the current window size.
    /// A resize reflows text already on screen, which a live region can't recover
    /// from in place, so the menu calls this and then redraws itself below.
    /// </summary>
    public static void Redraw()
    {
        AnsiConsole.Clear();
        foreach (var draw in HeaderParts) draw();
    }

    public static void Footer(string extra = "")
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule { Style = Theme.AccentStyle });
        var hints = "  [green]Up/Down[/][grey50] navigate  [/][green]Enter[/][grey50] select  [/][green]Esc[/][grey50] back[/]";
        if (!string.IsNullOrWhiteSpace(extra))
            hints = $"  {extra}  [grey50]·[/]  {hints.TrimStart()}";
        AnsiConsole.MarkupLine(hints);
        AnsiConsole.WriteLine();
    }

    public static void Working(string message = "Loading…  Please wait.")
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"  [grey50]{Markup.Escape(message)}[/]");
    }

    public static void Notice(string markup) => Add(() =>
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"  {markup}");
    });

    public static void PressAnyKey()
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("  [grey50]Press any key to continue...[/]");
        // ReadKey throws on redirected stdin (piped runs, CI). Don't crash the
        // host — just return as if the user pressed a key.
        try { System.Console.ReadKey(intercept: true); }
        catch (InvalidOperationException) { }
    }
}
