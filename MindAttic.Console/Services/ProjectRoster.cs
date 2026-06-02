using MindAttic.Console.Models;

namespace MindAttic.Console.Services;

public static class ProjectRoster
{
    // settings.Projects can be null even though the model initializes it: a
    // settings.json with an explicit "projects": null deserializes to null
    // (System.Text.Json overrides the initializer for a present-but-null key).
    // Coalesce so a hand-edited/tool-written file can't NRE every menu that
    // lists projects.
    public static IReadOnlyList<Project> Sorted(AppSettings settings) =>
        (settings.Projects ?? [])
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static Project? FindByName(AppSettings settings, string name) =>
        (settings.Projects ?? []).FirstOrDefault(p =>
            string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
}
