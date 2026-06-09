using System.Diagnostics;
using MindAttic.Console.Services;
using MindAttic.Console.Ui;
using Spectre.Console;

namespace MindAttic.Console.Menus;

/// <summary>
/// "Overlord" — one CLI to rule them all. Opens a single agent session rooted at
/// the MindAttic workspace root (<c>D:\Projects\MindAttic</c>), which holds every
/// project repo as a subdirectory. From there one agent can answer questions
/// about and give directions to any repo under the root — it scans recursively
/// itself, no fan-out and no per-project tab. An optional opening order is seeded
/// as the session's first prompt.
/// </summary>
public sealed class OverlordMenu(AgentProviderRegistry providers, WindowsTerminalLauncher wt)
{
    /// <summary>Tag the OpenProjectMenu attaches to its top "Overlord" row.</summary>
    public static readonly object MenuTag = new();

    public void Run()
    {
        Screen.Header("Overlord");

        var root = ResolveMindAtticRoot();
        if (!Directory.Exists(root))
        {
            Screen.Notice($"[red]Workspace root not found:[/] [grey50]{Markup.Escape(root)}[/]");
            Screen.PressAnyKey();
            return;
        }

        var draft = CollectDraft(root);
        if (draft is null) return; // stdin redirected or user cancelled

        if (string.IsNullOrWhiteSpace(draft))
        {
            LaunchTab(root, order: null);
            return;
        }

        if (!AnsiConsole.Confirm($"  [cyan1]Send this draft to the refiner?[/]", defaultValue: true))
        {
            // User declined refinement — launch with raw draft.
            LaunchTab(root, draft);
            return;
        }

        var refined = RefineWithClaude(draft);

        if (refined is null)
        {
            // Refiner failed; offer to fall back to the raw draft.
            Screen.Notice("[yellow]Refiner returned no output.[/] Launching with your original draft.");
            LaunchTab(root, draft);
            return;
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[cyan1]Refined order:[/]");
        AnsiConsole.MarkupLine($"[grey50]{Markup.Escape(refined)}[/]");
        AnsiConsole.WriteLine();

        string order;
        if (AnsiConsole.Confirm("  [cyan1]Use the refined order?[/]", defaultValue: true))
            order = refined;
        else if (AnsiConsole.Confirm("  [cyan1]Use your original draft instead?[/]", defaultValue: true))
            order = draft;
        else
            return; // user bailed

        LaunchTab(root, order);
    }

    // ── Input ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Collects a multi-line order from the user.  A blank line (Enter on an
    /// empty line) commits the draft.  Returns <c>null</c> when stdin is
    /// redirected or the user cancels with Ctrl+C.
    /// </summary>
    private static string? CollectDraft(string root)
    {
        AnsiConsole.MarkupLine($"  [cyan1]Order for the Overlord[/] [grey50](reaches every repo under {Markup.Escape(root)})[/]");
        AnsiConsole.MarkupLine("  [grey50]Type your order — multiple lines OK. Press Enter on a blank line when done.[/]");
        AnsiConsole.WriteLine();

        var lines = new List<string>();
        try
        {
            while (true)
            {
                AnsiConsole.Markup("  [cyan1]>[/] ");
                var line = System.Console.ReadLine();
                if (line is null) break;          // EOF / stdin redirected
                if (line.Length == 0) break;      // blank line = commit
                lines.Add(line);
            }
        }
        catch (InvalidOperationException)
        {
            return null;
        }

        return lines.Count == 0 ? string.Empty : string.Join("\n", lines);
    }

    // ── LLM refinement (subprocess) ────────────────────────────────────────────

    private const string RefinerSystemPrompt =
        "You are a precision prompt engineer. The user has written a rough directive for an " +
        "AI coding agent that will operate over a large multi-repo workspace. Transform the draft " +
        "into a single, clear, actionable agent directive. Preserve every intent; improve clarity, " +
        "specificity, and structure. Output ONLY the refined directive — no commentary, no preamble.";

    /// <summary>
    /// Calls <c>claude -p &lt;system&gt; &lt;user&gt;</c> via subprocess and returns
    /// captured stdout, or <c>null</c> if the process fails or times out.
    /// </summary>
    private static string? RefineWithClaude(string draft)
    {
        string? result = null;

        AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("cyan1"))
            .Start("[cyan1]Refining your order...[/]", ctx =>
            {
                try
                {
                    var psi = new ProcessStartInfo("claude")
                    {
                        ArgumentList = { "-p", RefinerSystemPrompt, draft },
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    };

                    using var proc = Process.Start(psi);
                    if (proc is null) return;

                    result = proc.StandardOutput.ReadToEnd().Trim();
                    proc.WaitForExit(60_000);

                    if (proc.ExitCode != 0 || string.IsNullOrWhiteSpace(result))
                        result = null;
                }
                catch (Exception)
                {
                    result = null;
                }
            });

        return result;
    }

    // ── Launch ─────────────────────────────────────────────────────────────────

    private void LaunchTab(string root, string? order)
    {
        var provider = providers.Current();

        Screen.Working();
        ExePath.EnsureFresh();
        wt.Open(wt.BuildAgentTabAtPath(
            $"Overlord [{provider.Key}]", root, provider, ExePath.Release,
            prompt: string.IsNullOrWhiteSpace(order) ? null : order));

        if (!string.IsNullOrWhiteSpace(order))
            Screen.Notice($"[green]Overlord tab opened[/] [grey50]({provider.Key} at {Markup.Escape(root)}).[/] " +
                          "[grey50]The order is pre-filled — press Enter in that tab to send it.[/]");
        else
            Screen.Notice($"[green]Overlord tab opened[/] [grey50]({provider.Key} at {Markup.Escape(root)}).[/]");

        Thread.Sleep(800); // PS launcher's anti-flicker wait
    }

    // ── Workspace resolution ───────────────────────────────────────────────────

    /// <summary>
    /// The directory under which every MindAttic project repo lives — the parent
    /// of this console's repo (the folder that holds <c>scripts/publish.ps1</c>).
    /// Falls back to the historical workspace path when run from an isolated exe
    /// with no detectable repo root.
    /// </summary>
    public static string ResolveMindAtticRoot()
    {
        var dir = Path.GetDirectoryName(Environment.ProcessPath ?? string.Empty);
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "scripts", "publish.ps1")))
                return Path.GetDirectoryName(dir) ?? dir; // parent of the console repo
            dir = Path.GetDirectoryName(dir);
        }
        return @"D:\Projects\MindAttic";
    }
}
