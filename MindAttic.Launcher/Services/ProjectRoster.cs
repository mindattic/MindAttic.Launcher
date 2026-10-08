using MindAttic.Launcher.Models;

namespace MindAttic.Launcher.Services;

public static class ProjectRoster
{
    // settings.Projects can be null even though the model initializes it: a
    // settings.json with an explicit "projects": null deserializes to null
    // (System.Text.Json overrides the initializer for a present-but-null key).
    // Coalesce so a hand-edited/tool-written file can't NRE every menu that
    // lists projects.
    public static IReadOnlyList<Project> Sorted(AppSettings settings) =>
        Sorted(settings, taggedFullNames: null);

    /// <summary>
    /// Same as <see cref="Sorted(AppSettings)"/>, but when
    /// <paramref name="taggedFullNames"/> is non-null (see
    /// <see cref="TopicRepoSync"/>), keeps only projects whose
    /// <see cref="Project.RepoUrl"/> resolves to one of those GitHub
    /// <c>owner/repo</c> full names — a repo tagged elsewhere, surfaced here;
    /// untagged, it quietly stops appearing without losing its settings.json
    /// entry. A project with no parseable GitHub remote can't be verified as
    /// tagged, so it's excluded too when a filter is active.
    /// </summary>
    public static IReadOnlyList<Project> Sorted(AppSettings settings, IReadOnlySet<string>? taggedFullNames)
    {
        IEnumerable<Project> projects = settings.Projects ?? [];
        if (taggedFullNames is not null)
            projects = projects.Where(p => GitHubRepoRef.Parse(p.RepoUrl) is { } full && taggedFullNames.Contains(full));

        return projects
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static Project? FindByName(AppSettings settings, string name) =>
        (settings.Projects ?? []).FirstOrDefault(p =>
            string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
}
