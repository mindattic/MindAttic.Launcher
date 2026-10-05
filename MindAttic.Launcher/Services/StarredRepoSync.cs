using MindAttic.Vault.Credentials;
using Spectre.Console;

namespace MindAttic.Launcher.Services;

/// <summary>
/// Resolves "which repos are currently starred on GitHub" once per launch, so
/// <see cref="ProjectRoster"/> and <see cref="Menus.DiscoverProjectsMenu"/> can
/// both filter against the same snapshot. Starring/unstarring on GitHub is the
/// user's lever: a repo surfaces in the roster once starred, and quietly stops
/// appearing (without losing its settings.json entry) once unstarred.
/// </summary>
public static class StarredRepoSync
{
    /// <summary>
    /// Null means "don't filter": no GitHub token is configured yet, or the
    /// fetch failed. Either way the roster degrades to unfiltered rather than
    /// silently emptying out — a network hiccup prints a notice instead of
    /// hiding every project.
    /// </summary>
    public static IReadOnlySet<string>? FetchOrNull(TokenStore? tokenStore = null, GitHubStarredRepos? client = null)
    {
        var token = (tokenStore ?? GitHubCredentials.DefaultStore).Get(GitHubCredentials.TokenName);
        if (string.IsNullOrWhiteSpace(token)) return null;

        try
        {
            return (client ?? new GitHubStarredRepos()).FetchAsync(token).GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
        {
            AnsiConsole.MarkupLine(
                $"  [yellow]Note:[/] [grey50]couldn't refresh starred repos from GitHub ({Markup.Escape(ex.Message)}); showing the full roster.[/]");
            return null;
        }
    }
}
