namespace MindAttic.Launcher.Services;

/// <summary>
/// Fills in <see cref="Models.Project.RepoUrl"/> for projects registered before
/// the GitHub-starred-repo filter existed. Without a RepoUrl, <see cref="GitHubRepoRef.Parse"/>
/// has nothing to match against <see cref="StarredRepoSync"/>'s set, so
/// <see cref="ProjectRoster.Sorted(Models.AppSettings, IReadOnlySet{string}?)"/> excludes the
/// project from every menu once the starred filter is active — even when it genuinely
/// is starred on GitHub. Resolves each blank RepoUrl from the project's local
/// <c>origin</c> remote (same lookup <see cref="Menus.DiscoverProjectsMenu"/> uses for new
/// repos) and persists it once, so the next launch's filter can see it.
/// </summary>
public static class RepoUrlBackfill
{
    public static void Run(SettingsStore store, Func<string, string?> resolveRemoteUrl)
    {
        var settings = store.Load();

        var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in settings.Projects ?? [])
        {
            if (!string.IsNullOrWhiteSpace(p.RepoUrl)) continue;
            if (resolveRemoteUrl(p.Path) is { } url && !string.IsNullOrWhiteSpace(url))
                resolved[p.Name] = url;
        }
        if (resolved.Count == 0) return;

        store.Update(s =>
        {
            foreach (var p in s.Projects ?? [])
                if (resolved.TryGetValue(p.Name, out var url))
                    p.RepoUrl = url;
        });
    }
}
