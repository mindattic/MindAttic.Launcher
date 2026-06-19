using MindAttic.Launcher.Models;

namespace MindAttic.Launcher.Services;

/// <summary>A git repo found under the MindAttic root that isn't in the roster.</summary>
public sealed record DiscoveredRepo(string Name, string Path);

/// <summary>
/// Scans the MindAttic workspace root for git repos that aren't yet registered
/// as projects, so new repos surface in the menus without a manual settings edit.
/// </summary>
public static class ProjectDiscovery
{
    /// <summary>
    /// Immediate subdirectories of <paramref name="root"/> that contain a git
    /// repo (a <c>.git</c> directory or, for worktrees, a <c>.git</c> file — the
    /// same test <see cref="GitService.Status"/> uses) and are neither already in
    /// <see cref="AppSettings.Projects"/> nor in <see cref="AppSettings.DiscoveryIgnore"/>.
    /// Returns an empty list when the root is missing.
    /// </summary>
    public static IReadOnlyList<DiscoveredRepo> FindUnregistered(AppSettings settings, string root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            return [];

        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in settings.Projects ?? [])
            if (!string.IsNullOrWhiteSpace(p.Path))
                known.Add(Normalize(p.Path));
        foreach (var ignored in settings.DiscoveryIgnore ?? [])
            if (!string.IsNullOrWhiteSpace(ignored))
                known.Add(Normalize(ignored));

        // Materialize the listing inside the guard: this runs at startup (the
        // discovery walk-through), and an unreadable root — a permission-denied
        // mount, a disconnected network drive — would otherwise throw straight
        // out of the menu's launch path and crash it. Degrade to "nothing found".
        string[] subdirs;
        try
        {
            subdirs = Directory.GetDirectories(root);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        var found = new List<DiscoveredRepo>();
        foreach (var dir in subdirs)
        {
            if (!IsGitRepo(dir)) continue;
            if (known.Contains(Normalize(dir))) continue;
            found.Add(new DiscoveredRepo(System.IO.Path.GetFileName(dir.TrimEnd(System.IO.Path.DirectorySeparatorChar)), dir));
        }

        return found
            .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsGitRepo(string dir)
    {
        var dotGit = System.IO.Path.Combine(dir, ".git");
        return Directory.Exists(dotGit) || File.Exists(dotGit);
    }

    /// <summary>
    /// Canonical comparison key for a path: full path, trailing separators
    /// stripped. Lets a settings entry written as <c>D:\…\Foo\</c> match a scanned
    /// <c>D:\…\Foo</c>.
    /// </summary>
    private static string Normalize(string path)
    {
        try
        {
            return System.IO.Path.GetFullPath(path)
                .TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // Un-normalizable junk in settings shouldn't crash discovery; compare
            // the raw string instead so it just won't match a real scanned path.
            return path.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
        }
    }
}
