using MindAttic.Launcher.Models;
using MindAttic.Launcher.Services;
using MindAttic.Launcher.Ui;
using Spectre.Console;

namespace MindAttic.Launcher.Menus;

/// <summary>
/// Global settings for CLI development: the model and API key each agent CLI
/// runs with, the GitHub token, and the Windows Terminal settings path.
/// </summary>
public sealed class SettingsMenu(AgentProviderRegistry providers, SettingsStore store)
{
    // Tag wrappers so a row's Tag type doesn't collide with a raw AgentProvider.
    private sealed record ModelTarget(AgentProvider Provider);
    private sealed record ApiKeyTarget(AgentProvider Provider);
    private static readonly object GitHubTokenTag = new();
    private static readonly object WindowsTerminalPathTag = new();

    public void Run()
    {
        var resumeIndex = 0;
        while (true)
        {
            var all = providers.All();
            var items = new List<MenuItem>();

            // Model + API key per agent CLI — the headline rows of this screen.
            foreach (var p in all)
            {
                var model = ProviderModel.Get(p.RunCommand);
                items.Add(new MenuItem
                {
                    Name = $"{p.Name} model",
                    Description = string.IsNullOrWhiteSpace(model) ? "(CLI default)" : model!,
                    Tag = new ModelTarget(p)
                });

                var hasKey = !string.IsNullOrWhiteSpace(LlmCredentials.GetKey(p.Key));
                var loggedIntoClaudeAi = IsClaude(p) && ClaudeAuthStatus.IsLoggedIntoClaudeAi();
                items.Add(new MenuItem
                {
                    Name = $"{p.Name} API key",
                    Description = loggedIntoClaudeAi
                        ? hasKey ? "configured — ignored while logged into claude.ai" : "(not set) — using your claude.ai login"
                        : hasKey ? "configured — shared with every MindAttic app" : "(not set) — CLI falls back to its own login/config",
                    Tag = new ApiKeyTarget(p)
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

            var wtPath = store.Load().WindowsTerminalSettingsPath;
            items.Add(new MenuItem
            {
                Name = "Windows Terminal settings path",
                Description = string.IsNullOrWhiteSpace(wtPath)
                    ? "(not set) — project tab colors won't be written until this is set"
                    : wtPath!,
                Tag = WindowsTerminalPathTag
            });

            Screen.Header("Settings");
            var result = Menu.PromptWithKeys("Configure CLI development:", items, customKeys: null, initialIndex: resumeIndex);
            resumeIndex = result.Index;
            var sel = result.Selected;
            if (sel is null) return;

            if (sel.Tag is ModelTarget target)
                EditModel(target.Provider);
            else if (sel.Tag is ApiKeyTarget keyTarget)
                EditApiKey(keyTarget.Provider);
            else if (ReferenceEquals(sel.Tag, GitHubTokenTag))
                EditGitHubToken();
            else if (ReferenceEquals(sel.Tag, WindowsTerminalPathTag))
                EditWindowsTerminalPath();
        }
    }

    private static bool IsClaude(AgentProvider provider) =>
        string.Equals(provider.Key, "Claude", StringComparison.OrdinalIgnoreCase);

    private static void EditApiKey(AgentProvider provider)
    {
        var current = LlmCredentials.GetKey(provider.Key);
        var loggedIntoClaudeAi = IsClaude(provider) && ClaudeAuthStatus.IsLoggedIntoClaudeAi();

        Screen.Header("Settings", provider.Name, "API key");
        AnsiConsole.MarkupLine($"  Current: [cyan1]{(string.IsNullOrWhiteSpace(current) ? "(not set)" : Mask(current))}[/]");
        AnsiConsole.MarkupLine("  [grey50]Stored in the shared MindAttic LLM keyring (%APPDATA%\\MindAttic\\LLM\\providers.json) —[/]");
        AnsiConsole.MarkupLine("  [grey50]the fallback every MindAttic app checks before its own login or config file.[/]");
        if (loggedIntoClaudeAi)
        {
            AnsiConsole.MarkupLine("  [yellow]You're logged into claude.ai in this CLI — that session wins over any key saved here.[/]");
            AnsiConsole.MarkupLine("  [yellow]Run 'claude /logout' first if you want this key to take effect instead.[/]");
        }
        AnsiConsole.WriteLine();

        var input = AnsiConsole.Prompt(
            new TextPrompt<string>("  [cyan1]API key[/] [grey50](blank to leave unchanged)[/]:").AllowEmpty());
        if (string.IsNullOrWhiteSpace(input))
        {
            Screen.Notice("[grey50]Unchanged.[/]");
            Screen.PressAnyKey();
            return;
        }

        LlmCredentials.SetKey(provider.Key, input.Trim());
        Screen.Notice($"[green]{Markup.Escape(provider.Name)} API key saved.[/]");
        Thread.Sleep(800);
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

    private void EditWindowsTerminalPath()
    {
        var current = store.Load().WindowsTerminalSettingsPath;
        var detected = WindowsTerminalSchemes.CandidateSettingsPaths.Where(File.Exists).ToList();

        var items = new List<MenuItem>();
        foreach (var candidate in detected)
        {
            items.Add(new MenuItem
            {
                Name = candidate,
                Description = string.Equals(candidate, current, StringComparison.OrdinalIgnoreCase)
                    ? "detected ← current"
                    : "detected",
                Tag = candidate
            });
        }
        items.Add(new() { Name = "Enter path manually…", Description = "type the exact path to Windows Terminal's settings.json", Tag = "custom" });
        items.Add(new() { Name = "Clear", Description = "unset — project tab colors stop being written", Tag = "clear" });

        Screen.Header("Settings", "Windows Terminal settings path");
        Screen.Add(() =>
        {
            AnsiConsole.MarkupLine($"  Current: [cyan1]{Markup.Escape(string.IsNullOrWhiteSpace(current) ? "(not set)" : current!)}[/]");
            AnsiConsole.MarkupLine("  [grey50]Used to splice a MindAttic-<Name> color scheme into Windows Terminal when a project tab opens.[/]");
            AnsiConsole.WriteLine();
        });

        var sel = Menu.Prompt("Set the Windows Terminal settings path:", items);
        if (sel is null) return;

        string? path;
        switch (sel.Tag)
        {
            case "clear":
                path = null;
                break;
            case "custom":
                AnsiConsole.WriteLine();
                path = AnsiConsole.Prompt(
                    new TextPrompt<string>("  [cyan1]Path[/]:")
                        .AllowEmpty()
                        .DefaultValue(current ?? "")
                        .ShowDefaultValue(false));
                if (string.IsNullOrWhiteSpace(path)) path = null;
                break;
            default:
                path = (string)sel.Tag!;
                break;
        }

        var warnMissing = !string.IsNullOrWhiteSpace(path) && !File.Exists(path);

        store.Update(s => s.WindowsTerminalSettingsPath = path);

        var saved = store.Load().WindowsTerminalSettingsPath;
        if (string.IsNullOrWhiteSpace(saved))
            Screen.Notice("[green]Windows Terminal settings path cleared.[/]");
        else if (warnMissing)
            Screen.Notice($"[yellow]Saved[/] [cyan1]{Markup.Escape(saved)}[/] [yellow]but no file exists there yet.[/]");
        else
            Screen.Notice($"[green]Windows Terminal settings path set to[/] [cyan1]{Markup.Escape(saved)}[/]");
        Thread.Sleep(800);
    }

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
        // Through Screen so a window resize repaints them above the list.
        Screen.Add(() =>
        {
            AnsiConsole.MarkupLine(
                $"  Current model: [cyan1]{Markup.Escape(string.IsNullOrWhiteSpace(current) ? "(CLI default)" : current!)}[/]");
            AnsiConsole.MarkupLine($"  [grey50]Command:[/] [grey50]{Markup.Escape(provider.RunCommand)}[/]");
            AnsiConsole.WriteLine();
        });

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
