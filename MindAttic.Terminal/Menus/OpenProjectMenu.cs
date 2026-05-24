using MindAttic.Terminal.Models;
using MindAttic.Terminal.Services;
using MindAttic.Terminal.Ui;

namespace MindAttic.Terminal.Menus;

public sealed class OpenProjectMenu(SettingsStore store, AgentProviderRegistry providers, WindowsTerminalLauncher wt)
{
    private static readonly IReadOnlySet<ConsoleKey> CustomKeys = new HashSet<ConsoleKey> { ConsoleKey.P };

    public void Run()
    {
        while (true)
        {
            var settings = store.Load();
            var sortedProjects = ProjectRoster.Sorted(settings);
            var items = sortedProjects
                .Select(p => new MenuItem
                {
                    Name = p.Name,
                    Description = DescribeProject(p, providers),
                    Tag = p
                })
                .ToList();

            Screen.Header("Open Project Tab");
            var result = Menu.PromptWithKeys(
                "Choose a project to open:",
                items,
                CustomKeys,
                extraHint: "[green]P[/][grey50] cycle provider[/]");

            if (result.Back) return;

            if (result.Selected is { } sel)
            {
                var project = (Project)sel.Tag!;
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
