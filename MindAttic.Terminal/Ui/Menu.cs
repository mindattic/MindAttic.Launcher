using Spectre.Console;

namespace MindAttic.Terminal.Ui;

public static class Menu
{
    /// <summary>
    /// Selection prompt with real keyboard handling: arrow keys / Home / End to
    /// move, Enter to select, Esc to go back. Returns the selected item, or
    /// null if the user pressed Esc (or the "&lt; Back" sentinel).
    /// </summary>
    public static MenuItem? Prompt(string title, IReadOnlyList<MenuItem> items, bool allowBack = true)
    {
        var result = PromptWithKeys(title, items, customKeys: null, allowBack: allowBack);
        return result.Selected;
    }

    /// <summary>
    /// Like <see cref="Prompt"/>, but also surfaces a configured set of "custom"
    /// keys (e.g. P to cycle provider on the project list). When a custom key is
    /// pressed the prompt exits with <see cref="MenuResult.CustomKey"/> set and
    /// <see cref="MenuResult.KeyTarget"/> pointing at the highlighted item, so
    /// the caller can react and re-enter the loop with fresh items.
    /// </summary>
    public static MenuResult PromptWithKeys(
        string title,
        IReadOnlyList<MenuItem> items,
        IReadOnlySet<ConsoleKey>? customKeys,
        bool allowBack = true,
        string? extraHint = null)
    {
        var rows = new List<MenuItem>(items);
        if (allowBack)
        {
            rows.Add(new MenuItem
            {
                Name = "< Back",
                Description = "return to previous menu",
                Tag = MenuSentinel.Back
            });
        }

        if (rows.Count == 0) return new MenuResult { Back = true };

        var nameWidth = rows.Max(i => i.Name.Length);
        var index = 0;
        var startTop = Console.CursorTop;

        var priorCursorVisible = true;
        try { priorCursorVisible = Console.CursorVisible; } catch { /* not supported on this stream */ }

        try
        {
            Console.CursorVisible = false;
            Render(title, rows, nameWidth, index, extraHint);

            while (true)
            {
                var keyInfo = Console.ReadKey(intercept: true);
                var navigated = false;

                switch (keyInfo.Key)
                {
                    case ConsoleKey.UpArrow:
                    case ConsoleKey.K:
                        index = index == 0 ? rows.Count - 1 : index - 1;
                        navigated = true;
                        break;
                    case ConsoleKey.DownArrow:
                    case ConsoleKey.J:
                        index = (index + 1) % rows.Count;
                        navigated = true;
                        break;
                    case ConsoleKey.Home:
                        index = 0;
                        navigated = true;
                        break;
                    case ConsoleKey.End:
                        index = rows.Count - 1;
                        navigated = true;
                        break;
                    case ConsoleKey.Enter:
                    {
                        var chosen = rows[index];
                        if (ReferenceEquals(chosen.Tag, MenuSentinel.Back))
                            return new MenuResult { Back = true };
                        return new MenuResult { Selected = chosen };
                    }
                    case ConsoleKey.Escape:
                        return new MenuResult { Back = true };
                    default:
                        if (customKeys is not null && customKeys.Contains(keyInfo.Key))
                        {
                            var target = rows[index];
                            if (!ReferenceEquals(target.Tag, MenuSentinel.Back))
                                return new MenuResult { CustomKey = keyInfo.Key, KeyTarget = target };
                        }
                        break;
                }

                if (!navigated) continue;

                Console.SetCursorPosition(0, startTop);
                Console.Write("\x1b[J");
                Render(title, rows, nameWidth, index, extraHint);
            }
        }
        finally
        {
            try { Console.CursorVisible = priorCursorVisible; } catch { /* see above */ }
        }
    }

    private static void Render(string title, IReadOnlyList<MenuItem> rows, int nameWidth, int highlighted, string? extraHint)
    {
        AnsiConsole.MarkupLine(title);
        AnsiConsole.WriteLine();
        for (var i = 0; i < rows.Count; i++)
            RenderItem(rows[i], nameWidth, i == highlighted);
        AnsiConsole.WriteLine();
        var hints = "[green]Up/Down[/][grey50] navigate  [/][green]Enter[/][grey50] select  [/][green]Esc[/][grey50] back[/]";
        if (!string.IsNullOrWhiteSpace(extraHint))
            hints = $"{extraHint}  [grey50]·[/]  {hints}";
        AnsiConsole.MarkupLine($"  {hints}");
    }

    private static void RenderItem(MenuItem item, int nameWidth, bool highlighted)
    {
        var name = item.Name.PadRight(nameWidth);
        var desc = string.IsNullOrWhiteSpace(item.Description) ? "" : $"  [grey50]{item.Description}[/]";
        if (highlighted)
            AnsiConsole.MarkupLine($"[yellow]> {name}[/]{desc}");
        else
            AnsiConsole.MarkupLine($"  {name}{desc}");
    }
}

public sealed class MenuResult
{
    public MenuItem? Selected { get; init; }
    public bool Back { get; init; }
    public ConsoleKey? CustomKey { get; init; }
    public MenuItem? KeyTarget { get; init; }
}

/// <summary>Marker tags for menu sentinels (Back, etc.).</summary>
public static class MenuSentinel
{
    public static readonly object Back = new();
}
