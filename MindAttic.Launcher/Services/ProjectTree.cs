using MindAttic.Launcher.Models;

namespace MindAttic.Launcher.Services;

/// <summary>
/// Groups a flat, dot-named roster (<c>MindAttic.Authentication</c>,
/// <c>GTFO.EZ.Shared</c>, ...) into collapsible levels by splitting names on
/// <c>.</c> and grouping by the next segment past the current <paramref
/// name="path"/>. A segment shared by more than one project becomes a
/// <see cref="GroupEntry"/> the caller can descend into by appending it to
/// the path; a segment only one project has becomes a leaf
/// <see cref="ProjectEntry"/>, labeled with whatever of the name is left
/// unconsumed (the full name at the root, just the remaining suffix once
/// inside a group). A project whose name exactly equals the current path
/// (e.g. <c>GTFO.EZ</c> itself, found while already inside the "EZ" group)
/// surfaces as a leaf alongside its own children rather than being folded
/// into them.
/// </summary>
public static class ProjectTree
{
    public abstract record Entry(string Name);

    public sealed record ProjectEntry(string Name, Project Project) : Entry(Name);

    public sealed record GroupEntry(string Name, int Count) : Entry(Name);

    public static IReadOnlyList<Entry> Level(IReadOnlyList<Project> projects, IReadOnlyList<string> path)
    {
        var depth = path.Count;
        var matching = new List<(Project Project, string[] Segments)>();
        foreach (var project in projects)
        {
            var segments = project.Name.Split('.');
            if (segments.Length < depth) continue;

            var isUnderPath = true;
            for (var i = 0; i < depth; i++)
            {
                if (!string.Equals(segments[i], path[i], StringComparison.OrdinalIgnoreCase))
                {
                    isUnderPath = false;
                    break;
                }
            }
            if (isUnderPath) matching.Add((project, segments));
        }

        var entries = new List<Entry>();
        foreach (var group in matching.GroupBy(
                     m => depth < m.Segments.Length ? m.Segments[depth] : null,
                     StringComparer.OrdinalIgnoreCase))
        {
            if (group.Key is null)
            {
                // Segments.Length == depth: the project's full name IS the
                // current path, not something past it.
                var selfName = depth > 0 ? path[depth - 1] : "";
                entries.AddRange(group.Select(m => new ProjectEntry(selfName, m.Project)));
                continue;
            }

            var items = group.ToList();
            entries.Add(items.Count == 1
                ? new ProjectEntry(string.Join('.', items[0].Segments.Skip(depth)), items[0].Project)
                : new GroupEntry(group.Key, items.Count));
        }

        return entries
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
