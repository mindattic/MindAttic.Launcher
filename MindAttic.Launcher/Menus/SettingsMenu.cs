using MindAttic.Launcher.Models;
using MindAttic.Launcher.Services;
using MindAttic.Launcher.Ui;
using Spectre.Console;

namespace MindAttic.Launcher.Menus;

/// <summary>
/// Global settings for CLI development: the model each agent CLI runs with.
/// </summary>
public sealed class SettingsMenu(AgentProviderRegistry providers)
{
    // Tag wrapper so a model row's Tag type doesn't collide with a raw AgentProvider.
    private sealed record ModelTarget(AgentProvider Provider);
    private static readonly object GitHubTokenTag = new();

    public void Run()
    {
        var resumeIndex = 0;
        while (true)
        {
            var all = providers.All();
            var items = new List<MenuItem>();

            // Model per agent CLI — the headline (and only row) of this screen.
            foreach (var p in all)
            {
                var model = ProviderModel.Get(p.RunCommand);
                items.Add(new MenuItem
                {
                    Name = $"{p.Name} model",
                    Description = string.IsNullOrWhiteSpace(model) ? "(CLI default)" : model!,
                    Tag = new ModelTarget(p)
                });
            }

            var hasToken = !string.IsNullOrWhiteSpace(GitHubCredentials.GetToken());
            items.Add(new MenuItem
            {
                Name = "GitHub token",
                Description = hasToken
                    ? $"configured — used to keep the roster synced to your '{TopicRepoSync.Topic}'-tagged repos"
                    : $"(not set) — used to keep the roster synced to your '{TopicRepoSync.Topic}'-tagged repos",
                Tag = GitHubTokenTag
            });

            Screen.Header("Settings");
            var result = Menu.PromptWithKeys("Configure CLI development:", items, customKeys: null, initialIndex: resumeIndex);
            resumeIndex = result.Index;
            var sel = result.Selected;
            if (sel is null) return;

            if (sel.Tag is ModelTarget target)
                EditModel(target.Provider);
            else if (ReferenceEquals(sel.Tag, GitHubTokenTag))
                EditGitHubToken();
        }
    }

    private static void EditGitHubToken()
    {
        var current = GitHubCredentials.GetToken();

        Screen.Header("Settings", "GitHub token");
        AnsiConsole.MarkupLine($"  Current: [cyan1]{(string.IsNullOrWhiteSpace(current) ? "(not set)" : Mask(current))}[/]");
        AnsiConsole.MarkupLine("  [grey50]A personal access token that can read your repos and their topics (private + public).[/]");
        AnsiConsole.MarkupLine($"  [grey50]Every launch re-checks GitHub, so adding or removing the '{TopicRepoSync.Topic}' topic on a repo there moves it into or out of the roster here.[/]");
        AnsiConsole.WriteLine();

        var input = AnsiConsole.Prompt(
            new TextPrompt<string>("  [cyan1]Token[/] [grey50](blank to leave unchanged)[/]:").AllowEmpty());
        if (string.IsNullOrWhiteSpace(input))
        {
            Screen.Notice("[grey50]Unchanged.[/]");
            Screen.PressAnyKey();
            return;
        }

        GitHubCredentials.SetToken(input.Trim());
        Screen.Notice("[green]GitHub token saved.[/]");
        Thread.Sleep(800);
    }

    private static string Mask(string token) =>
        token.Length <= 8 ? "••••" : $"{token[..4]}…{token[^4..]}";

    private void EditModel(AgentProvider provider)
    {
        var current = ProviderModel.Get(provider.RunCommand);
        AgentProviderRegistry.KnownModels.TryGetValue(provider.Key, out var knownModels);

        var items = new List<MenuItem>();
        foreach (var (id, label) in knownModels ?? [])
        {
            items.Add(new MenuItem
            {
                Name = id,
                Description = string.Equals(id, current, StringComparison.OrdinalIgnoreCase)
                    ? $"{label}  ← current"
                    : label,
                Tag = id
            });
        }
        items.Add(new() { Name = "Enter model id…", Description = "type the exact CLI model id", Tag = "custom" });
        items.Add(new() { Name = "Use CLI default", Description = "remove --model so the CLI picks", Tag = "clear" });

        Screen.Header("Settings", provider.Name, "Model");
        AnsiConsole.MarkupLine(
            $"  Current model: [cyan1]{Markup.Escape(string.IsNullOrWhiteSpace(current) ? "(CLI default)" : current!)}[/]");
        AnsiConsole.MarkupLine($"  [grey50]Command:[/] [grey50]{Markup.Escape(provider.RunCommand)}[/]");
        AnsiConsole.WriteLine();

        var sel = Menu.Prompt($"Set the model for {Markup.Escape(provider.Name)}:", items);
        if (sel is null) return;

        string? model;
        switch (sel.Tag)
        {
            case "clear":
                model = null;
                break;
            case "custom":
                AnsiConsole.WriteLine();
                model = AnsiConsole.Prompt(
                    new TextPrompt<string>("  [cyan1]Model id[/]:")
                        .AllowEmpty()
                        .DefaultValue(current ?? "")
                        .ShowDefaultValue(false));
                break;
            default:
                model = (string)sel.Tag!;
                break;
        }

        providers.SetModel(provider.Key, model);

        var saved = ProviderModel.Get(providers.ByKey(provider.Key)?.RunCommand);
        Screen.Notice(string.IsNullOrWhiteSpace(saved)
            ? $"[green]{Markup.Escape(provider.Name)} now uses the CLI default model.[/]"
            : $"[green]{Markup.Escape(provider.Name)} model set to[/] [cyan1]{Markup.Escape(saved)}[/]");
        Thread.Sleep(800);
    }
}
