using MindAttic.Vault.Credentials;
using Spectre.Console;

namespace MindAttic.Launcher.Services;

/// <summary>
/// Resolves "which repos currently carry the <see cref="Topic"/> topic on
/// GitHub" once per launch, so <see cref="ProjectRoster"/> and
/// <see cref="Menus.DiscoverProjectsMenu"/> can both filter against the same
/// snapshot. Tagging/untagging that topic on GitHub is the user's lever: a
/// repo surfaces in the roster once tagged, and quietly stops appearing
/// (without losing its settings.json entry) once the topic is removed.
/// </summary>
public static class TopicRepoSync
{
    /// <summary>The GitHub topic that marks a repo as part of the active roster.</summary>
    public const string Topic = "wip";

    /// <summary>
    /// Null means "don't filter": no GitHub token is configured yet, or the
    /// fetch failed. Either way the roster degrades to unfiltered rather than
    /// silently emptying out — a network hiccup prints a notice instead of
    /// hiding every project.
    /// </summary>
    public static IReadOnlySet<string>? FetchOrNull(TokenStore? tokenStore = null, GitHubTopicRepos? client = null)
    {
        var token = (tokenStore ?? GitHubCredentials.DefaultStore).Get(GitHubCredentials.TokenName);
        if (string.IsNullOrWhiteSpace(token)) return null;

        try
        {
            var repos = client ?? new GitHubTopicRepos();
            var result = repos.FetchAsync(token, Topic).GetAwaiter().GetResult();

            foreach (var name in repos.Healed)
                AnsiConsole.MarkupLine(
                    $"  [grey50]Note: removed the stale '{Topic}' topic from archived repo {Markup.Escape(name)}.[/]");

            if (repos.HealDenied)
                AnsiConsole.MarkupLine(
                    $"  [grey50]Note: an archived repo still carries the '{Topic}' topic, but the GitHub token can't edit topics " +
                    "(needs the classic 'repo' scope, or 'Administration: write' on a fine-grained token). It's still hidden from the roster.[/]");

            return result;
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
        {
            AnsiConsole.MarkupLine(
                $"  [yellow]Note:[/] [grey50]couldn't refresh {Topic}-tagged repos from GitHub ({Markup.Escape(ex.Message)}); showing the full roster.[/]");
            return null;
        }
    }
}
