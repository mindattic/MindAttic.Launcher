using MindAttic.Launcher.Models;
using MindAttic.Launcher.Services;
using MindAttic.Launcher.Ui;
using Spectre.Console;

namespace MindAttic.Launcher.Menus;

public sealed class OpenProjectMenu(SettingsStore store, AgentProviderRegistry providers, WindowsTerminalLauncher wt, IReadOnlySet<string>? tagged = null)
{
    private const string GeneralPromptTag = "general";

    /// <summary>Tags a row that descends into a sub-level rather than opening a project.</summary>
    private sealed record GroupTag(string Segment);

    public void Run()
    {
        // Dot-named projects (MindAttic.*, GTFO.*) collapse into a group row;
        // picking one pushes its segment onto the path and redraws one level
        // deeper. Esc pops the path before it falls back to exiting the menu.
        var path = new List<string>();
        var indexStack = new Stack<int>();
        var resumeIndex = 0;

        while (true)
        {
            var settings = store.Load();
            var sortedProjects = ProjectRoster.Sorted(settings, tagged);
            var entries = ProjectTree.Level(sortedProjects, path);

            var items = new List<MenuItem>();
            if (path.Count == 0)
            {
                items.Add(new MenuItem { Name = "General Prompt", Description = "open a CLI at the MindAttic workspace root (every project in context)", Tag = GeneralPromptTag });
            }
            items.AddRange(entries.Select((e, i) => ToMenuItem(e, spacerBefore: path.Count == 0 && i == 0)));

            Screen.Header(new[] { "Open Project Tab" }.Concat(path).ToArray());

            var result = Menu.PromptWithKeys(
                "Choose a project to open:",
                items,
                customKeys: null,
                initialIndex: resumeIndex);

            if (result.Back)
            {
                if (path.Count == 0) return;
                path.RemoveAt(path.Count - 1);
                resumeIndex = indexStack.Count > 0 ? indexStack.Pop() : 0;
                continue;
            }

            resumeIndex = result.Index;

            if (result.Selected is { } sel)
            {
                if (sel.Tag is string tag && tag == GeneralPromptTag)
                {
                    OpenGeneralPrompt();
                    continue;
                }

                if (sel.Tag is GroupTag group)
                {
                    indexStack.Push(result.Index);
                    path.Add(group.Segment);
                    resumeIndex = 0;
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

    /// <summary>A trailing disclosure marker (▸) is the only thing that tells a group row apart from a project row.</summary>
    private static MenuItem ToMenuItem(ProjectTree.Entry entry, bool spacerBefore) => entry switch
    {
        ProjectTree.GroupEntry g => new MenuItem
        {
            Name = $"{g.Name} ▸",
            Description = $"{g.Count} project{(g.Count == 1 ? "" : "s")}",
            Tag = new GroupTag(g.Name),
            SpacerBefore = spacerBefore
        },
        ProjectTree.ProjectEntry p => new MenuItem
        {
            Name = p.Name,
            Description = p.Project.Description ?? "",
            Tag = p.Project,
            SpacerBefore = spacerBefore
        },
        _ => throw new InvalidOperationException($"Unknown entry type: {entry.GetType()}")
    };

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
        // Who this opens as was already decided for the whole session at Launcher startup (M.LNCH-T71).

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
