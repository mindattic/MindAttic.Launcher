using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using MindAttic.Launcher.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace MindAttic.Launcher.Commands;

/// <summary>
/// Onboarding-only: saves a GitHub token (if one isn't already configured) and
/// tags every non-archived repo it can see with the <see cref="TopicRepoSync.Topic"/>
/// topic, so a brand-new user's roster is full on the very first launch instead
/// of requiring each repo to be tagged by hand on GitHub first. Invoked once by
/// setup.ps1 right after deploy, when the user supplied a token at the
/// "example repo" question — never run automatically again; from then on
/// tagging/untagging <c>wip</c> on GitHub is the user's own lever
/// (<see cref="TopicRepoSync"/>).
/// </summary>
[SuppressMessage("Performance", "CA1812", Justification = "Instantiated by Spectre.Console.Cli")]
public sealed class AdoptReposCommand : Command<AdoptReposCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("--token-env <NAME>")]
        [Description("Environment variable holding the GitHub token to save before tagging (default: MINDATTIC_GITHUB_TOKEN). Ignored if a token is already configured. Read from an env var rather than a CLI option so the token doesn't show up in process-list logging.")]
        public string TokenEnv { get; init; } = "MINDATTIC_GITHUB_TOKEN";
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var token = GitHubCredentials.GetToken();
        if (string.IsNullOrWhiteSpace(token))
        {
            token = Environment.GetEnvironmentVariable(settings.TokenEnv);
            if (string.IsNullOrWhiteSpace(token))
            {
                AnsiConsole.MarkupLine("[red]No GitHub token configured and none found in the environment — nothing to adopt repos with.[/]");
                return 1;
            }
            token = token.Trim();
            GitHubCredentials.SetToken(token);
        }

        try
        {
            var client = new GitHubTopicRepos();
            var result = client.AdoptAllAsync(token, TopicRepoSync.Topic).GetAwaiter().GetResult();

            AnsiConsole.MarkupLine(result.Tagged > 0
                ? $"[green]Tagged {result.Tagged} repo(s) as '{TopicRepoSync.Topic}'[/]" +
                  (result.AlreadyTagged > 0 ? $" [grey50]({result.AlreadyTagged} already tagged)[/]." : ".")
                : $"[grey50]Nothing to tag — {result.AlreadyTagged} repo(s) already carry '{TopicRepoSync.Topic}'.[/]");

            foreach (var name in result.Failed)
                AnsiConsole.MarkupLine($"  [yellow]Couldn't tag {Markup.Escape(name)}[/]");

            return result.Failed.Count == 0 ? 0 : 1;
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
        {
            AnsiConsole.MarkupLine($"[red]Couldn't reach GitHub: {Markup.Escape(ex.Message)}[/]");
            return 1;
        }
    }
}
