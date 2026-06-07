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

        string order;
        try
        {
            order = AnsiConsole.Prompt(
                new TextPrompt<string>($"  [cyan1]Order for the Overlord (reaches every repo under {Markup.Escape(root)}):[/]")
                    .AllowEmpty());
        }
        catch (InvalidOperationException)
        {
            // stdin redirected (piped run / CI) — nothing to read, bail cleanly.
            return;
        }

        // Root the agent at the workspace directly via host --path: no synthetic
        // roster entry (the old approach passed --name Overlord, which the host
        // couldn't find in settings.json, so the tab died on launch).
        var provider = providers.Current();

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
