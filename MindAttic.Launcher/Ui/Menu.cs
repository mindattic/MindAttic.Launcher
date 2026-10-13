using Spectre.Console;
using Spectre.Console.Rendering;

namespace MindAttic.Launcher.Ui;

public static class Menu
{
    // Lines the menu draws around its list: title, the panel's top and bottom
    // border, the hint line, plus one spare so the frame never touches the last
    // row (writing there scrolls the window and smears the live region).
    private const int ChromeLines = 5;
    private const int MinVisibleRows = 3;

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
    /// keys (e.g. S/N to skip color prompts during project discovery). When a
    /// custom key is pressed the prompt exits with <see cref="MenuResult.CustomKey"/>
    /// set and <see cref="MenuResult.KeyTarget"/> pointing at the highlighted item,
    /// so the caller can react and re-enter the loop with fresh items.
    /// </summary>
    /// <remarks>
    /// Drawn as a Spectre live region: each key press swaps the region's content
    /// in place instead of erasing and reprinting it, so navigation doesn't
    /// flicker. Lists taller than the window scroll inside the panel.
    /// </remarks>
    public static MenuResult PromptWithKeys(
        string title,
        IReadOnlyList<MenuItem> items,
        IReadOnlySet<ConsoleKey>? customKeys,
        bool allowBack = true,
        string? extraHint = null,
        CancellationToken refreshToken = default,
        int initialIndex = 0)
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

        // ReadKey/KeyAvailable throw on redirected stdin (piped runs, CI). Bail
        // out as "back" rather than starting a live region nothing can drive.
        if (System.Console.IsInputRedirected) return new MenuResult { Back = true };

        var index = Math.Clamp(initialIndex, 0, rows.Count - 1);
        // The region starts wherever the caller's header left the cursor; the
        // list gets whatever window height is left below that.
        var regionTop = 0;
        try { regionTop = System.Console.CursorTop - System.Console.WindowTop; } catch { /* redirected stdout */ }

        var priorCursorVisible = true;
        try { priorCursorVisible = System.Console.CursorVisible; } catch { /* not supported on this stream */ }

        MenuResult? result = null;
        try
        {
            System.Console.CursorVisible = false;
            AnsiConsole.Live(Build(title, rows, index, extraHint, regionTop))
                .AutoClear(false)
                .Overflow(VerticalOverflow.Ellipsis)
                .Start(ctx =>
                {
                    // Live only paints on Refresh/UpdateTarget; without this the
                    // menu stays invisible until the first key press.
                    ctx.Refresh();
                    var shownSecond = DateTime.Now.Second;
                    while (result is null)
                    {
                        // Tick the clock: repaint once a second even while idle.
                        if (DateTime.Now.Second != shownSecond)
                        {
                            shownSecond = DateTime.Now.Second;
                            ctx.UpdateTarget(Build(title, rows, index, extraHint, regionTop));
                        }

                        // Poll for a keypress so a refreshToken cancellation (e.g. the
                        // 60-second main-menu redraw timer) can interrupt the wait.
                        bool available;
                        try { available = System.Console.KeyAvailable; }
                        catch (InvalidOperationException) { result = new MenuResult { Back = true }; break; }

                        if (!available)
                        {
                            if (refreshToken.IsCancellationRequested)
                                result = new MenuResult { Timeout = true, Index = index };
                            else
                                Thread.Sleep(50);
                            continue;
                        }

                        ConsoleKeyInfo keyInfo;
                        try { keyInfo = System.Console.ReadKey(intercept: true); }
                        catch (InvalidOperationException) { result = new MenuResult { Back = true }; break; }

                        var moved = Navigate(keyInfo.Key, rows.Count, VisibleRows(regionTop), ref index);
                        if (moved)
                        {
                            ctx.UpdateTarget(Build(title, rows, index, extraHint, regionTop));
                            continue;
                        }

                        result = Resolve(keyInfo.Key, rows, index, allowBack, customKeys);
                    }
                });
        }
        finally
        {
            try { System.Console.CursorVisible = priorCursorVisible; } catch { /* see above */ }
        }

        return result ?? new MenuResult { Back = true };
    }

    /// <summary>Moves the highlight for navigation keys; returns false for any other key.</summary>
    private static bool Navigate(ConsoleKey key, int count, int pageSize, ref int index)
    {
        var next = key switch
        {
            ConsoleKey.UpArrow or ConsoleKey.K => index == 0 ? count - 1 : index - 1,
            ConsoleKey.DownArrow or ConsoleKey.J => (index + 1) % count,
            ConsoleKey.Home => 0,
            ConsoleKey.End => count - 1,
            ConsoleKey.PageUp => Math.Max(0, index - pageSize),
            ConsoleKey.PageDown => Math.Min(count - 1, index + pageSize),
            _ => -1
        };
        if (next < 0) return false;
        index = next;
        return true;
    }

    /// <summary>Maps a non-navigation key to a result, or null to keep waiting.</summary>
    private static MenuResult? Resolve(
        ConsoleKey key, IReadOnlyList<MenuItem> rows, int index, bool allowBack, IReadOnlySet<ConsoleKey>? customKeys)
    {
        switch (key)
        {
            case ConsoleKey.Enter:
            {
                var chosen = rows[index];
                if (ReferenceEquals(chosen.Tag, MenuSentinel.Back))
                    return new MenuResult { Back = true, Index = index };
                return new MenuResult { Selected = chosen, Index = index };
            }
            case ConsoleKey.Escape:
                return allowBack ? new MenuResult { Back = true, Index = index } : null;
            default:
                if (customKeys is not null && customKeys.Contains(key))
                {
                    var target = rows[index];
                    if (!ReferenceEquals(target.Tag, MenuSentinel.Back))
                        return new MenuResult { CustomKey = key, KeyTarget = target, Index = index };
                }
                return null;
        }
    }

    private static int VisibleRows(int regionTop)
    {
        var height = 25;
        try { height = System.Console.WindowHeight; } catch { /* redirected stdout */ }
        return Math.Max(MinVisibleRows, height - regionTop - ChromeLines);
    }

    private static IRenderable Build(string title, IReadOnlyList<MenuItem> rows, int highlighted, string? extraHint, int regionTop)
    {
        // Spectre's profile width is the console's width (80 when redirected), and
        // it's the width the live region actually renders at.
        var width = AnsiConsole.Profile.Width;

        // "< Back" sits apart from the items, below a blank spacer line.
        var hasBack = rows.Count > 1 && ReferenceEquals(rows[^1].Tag, MenuSentinel.Back);

        // Recomputed every frame so a resized window gets the right page size.
        // The spacer line costs a row, so reserve it whenever the list has Back.
        var visible = Math.Max(MinVisibleRows, VisibleRows(regionTop) - (hasBack ? 1 : 0));
        var first = Math.Clamp(highlighted - visible / 2, 0, Math.Max(0, rows.Count - visible));
        var last = Math.Min(rows.Count, first + visible);
        var scrolls = rows.Count > visible;

        // Rows are laid out by hand to exact widths rather than with a Spectre
        // Table: a table collapses entirely to "…" when content doesn't fit and
        // spreads spare width over every column. Here each line is exactly the
        // panel's inner width, and anything too wide is cut off with an ellipsis.
        // Inner width = window minus the two borders and the 1-cell left padding
        // (no right padding, so the scrollbar sits against the right border).
        var inner = Math.Max(10, width - 3);
        var contentWidth = inner - 2; // last two cells: a gap, then the scrollbar (or blank)
        var nameWidth = Math.Min(rows.Max(r => r.Name.Length), Math.Max(8, contentWidth / 2));
        var descWidth = Math.Max(0, contentWidth - 2 - nameWidth - 2); // "> " name "  " desc

        // Thumb spans the drawn lines (spacer included) so it reaches the bottom edge.
        var thumb = Thumb(rows.Count, visible, first, visible + (hasBack ? 1 : 0));
        var lines = new List<IRenderable>();
        for (var i = first; i < last; i++)
        {
            if (hasBack && i == rows.Count - 1 && i > first)
                lines.Add(new Markup(new string(' ', contentWidth + 1) + ScrollCell(lines.Count, thumb, scrolls)));

            // Name + Description come from user-controlled settings (project names,
            // RunCommand strings, etc.) — truncate first, then escape before
            // interpolating into markup so a stray '[' doesn't crash the prompt.
            var nameText = Truncate(rows[i].Name, nameWidth);
            var name = Markup.Escape(nameText);
            var desc = string.IsNullOrWhiteSpace(rows[i].Description) ? "" : Truncate(rows[i].Description!, descWidth);
            var pad = new string(' ', nameWidth - nameText.Length + 1);
            var tail = new string(' ', descWidth - desc.Length + 1);
            // The '>' keeps the selection visible when color is off (NO_COLOR).
            var label = i == highlighted ? $"[black on yellow]> {name} [/]" : $"  {name} ";
            lines.Add(new Markup($"{label}{pad}[grey50]{Markup.Escape(desc)}[/]{tail}{ScrollCell(lines.Count, thumb, scrolls)}"));
        }

        var panel = new Panel(new Rows(lines))
            .Border(BoxBorder.Rounded)
            .BorderStyle(Theme.DimStyle)
            .Padding(1, 0, 0, 0)
            .Expand();
        if (scrolls) panel.Header($"[grey50] {highlighted + 1}/{rows.Count} [/]");

        var hints = "[green]Up/Down[/][grey50] navigate  [/][green]Enter[/][grey50] select  [/][green]Esc[/][grey50] back[/]";
        if (!string.IsNullOrWhiteSpace(extraHint))
            hints = $"{extraHint}  [grey50]·[/]  {hints}";

        // Title on the left, live clock on the right edge, padded by hand. Both the
        // title and hint lines get cut off with an ellipsis rather than wrapping.
        var clock = $"{DateTime.Now:ddd h:mm:ss tt}";
        var titleRoom = width - 1 - clock.Length - 2;
        var titleText = Fit(title, titleRoom);
        var gap = Math.Max(2, width - 1 - Markup.Remove(titleText).Length - clock.Length);
        var titleLine = titleRoom < 8
            ? new Markup(Fit(title, width - 1))
            : new Markup($"{titleText}{new string(' ', gap)}[grey50]{clock}[/]");

        return new Rows(
            titleLine,
            panel,
            new Markup("  " + Fit(hints, width - 3)));
    }

    /// <summary>
    /// Scrollbar thumb as (start line, length) within the visible window: its
    /// length is the visible share of the list, its offset how far down it is.
    /// Display only; it isn't clickable.
    /// </summary>
    private static (int Start, int Length) Thumb(int total, int visible, int first, int trackLines)
    {
        if (total <= visible) return (0, trackLines);
        var length = Math.Max(1, (int)Math.Round((double)trackLines * visible / total));
        var start = (int)Math.Round((double)first * (trackLines - length) / (total - visible));
        return (start, length);
    }

    private static string Truncate(string text, int max) =>
        max <= 0 ? "" : text.Length <= max ? text : max == 1 ? "…" : text[..(max - 1)] + "…";

    /// <summary>
    /// Fits a markup string into <paramref name="max"/> cells. If it's too wide, the
    /// styling is dropped and the plain text is cut off with an ellipsis (cutting
    /// inside markup tags would leave them unbalanced).
    /// </summary>
    private static string Fit(string markup, int max)
    {
        var plain = Markup.Remove(markup);
        return plain.Length <= max ? markup : Markup.Escape(Truncate(plain, max));
    }

    private static string ScrollCell(int line, (int Start, int Length) thumb, bool scrolls) =>
        !scrolls ? " "
        : line >= thumb.Start && line < thumb.Start + thumb.Length ? "[grey70]┃[/]"
        : "[grey30]│[/]";
}

public sealed class MenuResult
{
    public MenuItem? Selected { get; init; }
    public bool Back { get; init; }
    public bool Timeout { get; init; }
    public ConsoleKey? CustomKey { get; init; }
    public MenuItem? KeyTarget { get; init; }
    public int Index { get; init; }
}

/// <summary>Marker tags for menu sentinels (Back, etc.).</summary>
public static class MenuSentinel
{
    public static readonly object Back = new();
}
