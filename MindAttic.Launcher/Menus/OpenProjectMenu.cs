using MindAttic.Launcher.Models;
using MindAttic.Launcher.Services;
using MindAttic.Launcher.Ui;
using Spectre.Console;

namespace MindAttic.Launcher.Menus;

public sealed class OpenProjectMenu(SettingsStore store, AgentProviderRegistry providers, WindowsTerminalLauncher wt, IReadOnlySet<string>? tagged = null)
{
    private const string GeneralPromptTag = "general";

    public void Run()
    {
        var resumeIndex = 0;
        while (true)
        {
            var settings = store.Load();
            var sortedProjects = ProjectRoster.Sorted(settings, tagged);

            var items = new List<MenuItem>
            {
                new() { Name = "General Prompt", Description = "open a CLI at the MindAttic workspace root (every project in context)", Tag = GeneralPromptTag },
            };
            items.AddRange(sortedProjects.Select((p, i) => new MenuItem
            {
                Name = p.Name,
                Description = p.Description ?? "",
                Tag = p,
                SpacerBefore = i == 0
            }));

            Screen.Header("Open Project Tab");

            var result = Menu.PromptWithKeys(
                "Choose a project to open:",
                items,
                customKeys: null,
                initialIndex: resumeIndex);

            resumeIndex = result.Index;

            if (result.Back) return;

            if (result.Selected is { } sel)
            {
                if (sel.Tag is string tag && tag == GeneralPromptTag)
                {
                    OpenGeneralPrompt();
                    continue;
                }

                var project = (Project)sel.Tag!;
                // Provider is an ephemeral, per-launch choice — nothing is
                // persisted. Esc/Back from the picker just returns here.
                var provider = PickProvider(project.Name, project.Name);
                if (provider is null) continue;

                new ProjectActionMenu(store, wt, project, provider).Run();
                continue;
            }
        }
    }

    /// <summary>
    /// Opens a host tab rooted at the MindAttic workspace root (not any single
    /// project) — every repo under it is in reach of the same CLI session.
    /// Mirrors MainMenuCommand's "Status" tab, minus the pre-filled prompt.
    /// </summary>
    private void OpenGeneralPrompt()
    {
        var root = ExePath.WorkspaceRoot;
        if (!Directory.Exists(root))
        {
            Screen.Notice($"[red]Workspace root not found:[/] [grey50]{Markup.Escape(root)}[/]");
            Screen.PressAnyKey();
            return;
        }

        var provider = PickProvider("General Prompt", "the workspace");
        if (provider is null) return;

        Screen.Working();
        ExePath.EnsureFresh();
        wt.Open(wt.BuildAgentTabAtPath($"General [{provider.Key}]", root, provider, ExePath.Release));
        Thread.Sleep(800);
    }

    /// <summary>
    /// Which CLI to launch with, decided fresh every time — Claude always sorts
    /// first (see <see cref="AgentProviderRegistry.Defaults"/>), then Codex,
    /// then Gemini, then Kimi.
    /// </summary>
    private AgentProvider? PickProvider(string headerSubtitle, string promptLabel)
    {
        var items = providers.All()
            .Select(p => new MenuItem { Name = p.Name, Description = p.RunCommand, Tag = p })
            .ToList();

        Screen.Header("Open Project Tab", headerSubtitle);
        var sel = Menu.Prompt($"Open {Markup.Escape(promptLabel)} with which agent?", items);
        return sel?.Tag as AgentProvider;
    }
}
