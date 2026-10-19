using System.Text;
using Spectre.Console;

namespace MindAttic.Launcher.Ui;

public static class Prompt
{
    /// <summary>
    /// A single-line text prompt where Esc cancels (returns null) instead of being
    /// ignored, the way Spectre's own <see cref="TextPrompt{T}"/> treats it. Enter on
    /// an empty line returns "" — the caller's own "blank" meaning (e.g. auto-generate)
    /// — which stays distinct from a null cancel.
    /// </summary>
    public static string? TextOrCancel(string label)
    {
        AnsiConsole.Markup(label);

        // ReadKey throws on redirected stdin (piped runs, CI); fall back to a plain
        // line read, where Esc can't be pressed so there's nothing to cancel.
        if (System.Console.IsInputRedirected) return System.Console.ReadLine() ?? "";

        var text = new StringBuilder();
        while (true)
        {
            var key = System.Console.ReadKey(intercept: true);
            switch (key.Key)
            {
                case ConsoleKey.Enter:
                    AnsiConsole.WriteLine();
                    return text.ToString();
                case ConsoleKey.Escape:
                    AnsiConsole.WriteLine();
                    return null;
                case ConsoleKey.Backspace:
                    if (text.Length > 0)
                    {
                        text.Length--;
                        System.Console.Write("\b \b");
                    }
                    break;
                default:
                    if (!char.IsControl(key.KeyChar))
                    {
                        text.Append(key.KeyChar);
                        System.Console.Write(key.KeyChar);
                    }
                    break;
            }
        }
    }
}
