using Spectre.Console;

namespace MindAttic.Console.Ui;

/// <summary>
/// Header / footer helpers that match the original PS layout: breadcrumb
/// title across the top, dim key-hint footer across the bottom.
/// </summary>
public static class Screen
{
    public static void Header(params string[] breadcrumbs)
    {
        AnsiConsole.Clear();
        var trail = string.Join(" > ", new[] { "MindAttic Console" }.Concat(breadcrumbs));
        AnsiConsole.Write(new Rule($"[cyan1]{Markup.Escape(trail)}[/]")
        {
            Style = Theme.AccentStyle,
            Justification = Justify.Left
        });
        AnsiConsole.WriteLine();
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

    public static void Notice(string markup)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"  {markup}");
    }

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
