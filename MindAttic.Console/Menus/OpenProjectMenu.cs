using MindAttic.Console.Models;
using MindAttic.Console.Services;
using MindAttic.Console.Ui;
using Spectre.Console;

namespace MindAttic.Console.Menus;

public sealed class OpenProjectMenu(SettingsStore store, AgentProviderRegistry providers, WindowsTerminalLauncher wt)
{
    private static readonly IReadOnlySet<ConsoleKey> CustomKeys = new HashSet<ConsoleKey> { ConsoleKey.P };

    public void Run()
    {
        while (true)
        {
            var settings = store.Load();
            var sortedProjects = ProjectRoster.Sorted(settings);

            // Overlord rides at the top of the list: one agent session rooted
            // at the MindAttic workspace, so a single order reaches every repo
            // under it without opening a tab per project.
            var items = new List<MenuItem>
            {
                new()
                {
                    Name = "Overlord",
                    Description = "open one agent session over the whole MindAttic workspace",
                    Tag = OverlordMenu.MenuTag
                }
            };
            items.AddRange(sortedProjects.Select(p => new MenuItem
            {
                Name = p.Name,
                Description = DescribeProject(p, providers),
                Tag = p
            }));

            Screen.Header("Open Project Tab");
            // Don't bail when the roster is empty — the Overlord row sits over the
            // whole workspace and needs no registered project, so it must stay
            // reachable here. Just note that nothing else is configured yet.
            if (sortedProjects.Count == 0)
                Screen.Notice("[grey50]No projects configured yet — only the workspace-wide Overlord is available.[/]");

            var result = Menu.PromptWithKeys(
                "Choose a project to open:",
                items,
                CustomKeys,
                extraHint: "[green]P[/][grey50] cycle provider[/]");

            if (result.Back) return;

            if (result.Selected is { } sel)
            {
                if (ReferenceEquals(sel.Tag, OverlordMenu.MenuTag))
                {
                    new OverlordMenu(providers, wt).Run();
                    continue;
                }

                var project = (Project)sel.Tag!;
                if (!Directory.Exists(project.Path))
                {
                    // wt opened with -d <missing path> fails to spawn the tab and
                    // the user sees nothing happen. Say why instead, matching the
                    // path-not-found checks in CommitMenu/PullMenu.
                    Screen.Notice($"[red]Path not found:[/] [grey50]{Markup.Escape(project.Path)}[/]");
                    Screen.PressAnyKey();
                    continue;
                }
                var provider = providers.EffectiveProvider(project);
                // Match RestartInNewTab: republish if source changed, then spawn
                // the canonical Release exe from artifacts/. Using ExePath.Self
                // here meant dev tabs ran whatever Debug bin happened to be live.
                ExePath.EnsureFresh();
                wt.Open(wt.BuildAgentTab(project, provider, ExePath.Release));
                Thread.Sleep(800); // PS launcher's anti-flicker wait
                continue;
            }

            if (result.CustomKey == ConsoleKey.P && result.KeyTarget?.Tag is Project target)
            {
                var current = providers.EffectiveProviderKey(target);
                var next = providers.Next(current);
                providers.SetProjectProvider(target.Name, next.Key);
                // Outer loop rebuilds items so the new provider shows in the description.
            }
        }
    }

    private static string DescribeProject(Project p, AgentProviderRegistry providers)
    {
        var providerKey = providers.EffectiveProviderKey(p);
        var providerLabel = string.IsNullOrWhiteSpace(p.Provider) ? $"default: {providerKey}" : providerKey;
        return string.IsNullOrWhiteSpace(p.Description) ? providerLabel : $"{providerLabel} - {p.Description}";
    }
}
