using MindAttic.Terminal.Models;

namespace MindAttic.Terminal.Services;

public static class ProjectRoster
{
    public static IReadOnlyList<Project> Sorted(AppSettings settings) =>
        settings.Projects
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static Project? FindByName(AppSettings settings, string name) =>
        settings.Projects.FirstOrDefault(p =>
            string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
}
