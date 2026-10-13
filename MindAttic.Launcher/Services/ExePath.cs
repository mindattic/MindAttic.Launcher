using System.Diagnostics;

namespace MindAttic.Launcher.Services;

/// <summary>
/// Resolves paths to the running and canonical MindAttic.Launcher executables.
/// <see cref="Self"/> is whatever exe is currently executing (e.g. the Debug
/// bin during dev). <see cref="Release"/> is the deployed single-file exe in
/// <see cref="DeployDir"/>, which the desktop shortcut's deploy.bat runs and
/// which agent tabs and "Restart" launch. <see cref="EnsureFresh"/> shells out to
/// the repo's <c>deploy.ps1</c> to republish when source has changed.
/// </summary>
public static class ExePath
{
    /// <summary>Where the Launcher is deployed, alongside every other MindAttic app.</summary>
    public const string DeployDir = @"C:\Apps\MindAttic.Launcher";

    /// <summary>
    /// File deploy.ps1 writes next to the deployed exe, holding the source repo's
    /// path. The deployed exe has no repo above it to walk up to, so this is how it
    /// finds deploy.ps1, scripts\, the workspace root and git.
    /// </summary>
    public const string RepoPathFile = "repo.path";

    public static string Self => Environment.ProcessPath ?? "MindAttic.Launcher";

    public static string Release
    {
        get
        {
            var deployed = Path.Combine(DeployDir, "MindAttic.Launcher.exe");
            return File.Exists(deployed) ? deployed : Self;
        }
    }

    /// <summary>
    /// The MindAttic.Launcher repo root (the directory holding <c>deploy.ps1</c>
    /// and the solution). Null when it can't be found. Used by
    /// <see cref="BuildFreshness"/> to compare the build against the latest commit.
    /// </summary>
    public static string? RepoRoot => FindRepoRoot();

    /// <summary>
    /// The directory under which every MindAttic project repo lives (e.g.
    /// <c>D:\Projects\MindAttic</c>) — the parent of <see cref="RepoRoot"/>.
    /// Falls back to the historical path when the repo can't be found.
    /// </summary>
    public static string WorkspaceRoot
    {
        get
        {
            var root = FindRepoRoot();
            if (root is null) return @"D:\Projects\MindAttic";
            return Path.GetDirectoryName(root) ?? root;
        }
    }

    public static void EnsureFresh()
    {
        var root = FindRepoRoot();
        if (root is null) return;
        var script = Path.Combine(root, "deploy.ps1");
        if (!File.Exists(script)) return;

        var psi = new ProcessStartInfo("powershell")
        {
            UseShellExecute = false,
            WorkingDirectory = root,
            // Capture the publish chatter instead of letting it scribble over the
            // Spectre menu we return to. RedirectStandardOutput also means we must
            // drain both streams, or a verbose publish can deadlock on a full pipe.
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-ExecutionPolicy");
        psi.ArgumentList.Add("Bypass");
        psi.ArgumentList.Add("-File");
        psi.ArgumentList.Add(script);

        try
        {
            using var p = Process.Start(psi);
            if (p is not null)
            {
                var drainOut = Task.Run(() => p.StandardOutput.ReadToEnd());
                var drainErr = Task.Run(() => p.StandardError.ReadToEnd());
                // Cap the wait so a wedged publish (nuget stall, AV scan) can't
                // freeze Restart / Open Project Tab. On timeout, kill and move on
                // — the caller still tries to launch whatever exe currently exists.
                if (!p.WaitForExit((int)TimeSpan.FromMinutes(2).TotalMilliseconds))
                {
                    try { p.Kill(entireProcessTree: true); } catch { }
                }
                try { Task.WaitAll(new[] { drainOut, drainErr }, TimeSpan.FromSeconds(5)); } catch { }
            }
        }
        catch
        {
            // Best-effort republish — if powershell is missing or the script
            // fails, the caller still tries to launch whatever exe exists.
        }
    }

    private static string? FindRepoRoot() =>
        ResolveRepoRoot(Path.GetDirectoryName(Environment.ProcessPath ?? string.Empty));

    /// <summary>
    /// Finds the repo for an exe in <paramref name="exeDir"/>: walk up from it (a
    /// dev build under bin\), else read the <see cref="RepoPathFile"/> a deploy
    /// wrote next to the exe. Either way the result must actually be the repo.
    /// </summary>
    public static string? ResolveRepoRoot(string? exeDir)
    {
        if (string.IsNullOrEmpty(exeDir)) return null;

        for (var dir = exeDir; !string.IsNullOrEmpty(dir); dir = Path.GetDirectoryName(dir))
        {
            if (IsRepo(dir)) return dir;
        }

        try
        {
            var marker = Path.Combine(exeDir, RepoPathFile);
            if (!File.Exists(marker)) return null;
            var recorded = File.ReadAllText(marker).Trim();
            return IsRepo(recorded) ? recorded : null;
        }
        catch
        {
            return null; // unreadable marker: behave as if there's no repo
        }
    }

    private static bool IsRepo(string dir) =>
        !string.IsNullOrWhiteSpace(dir)
        && File.Exists(Path.Combine(dir, "deploy.ps1"))
        && File.Exists(Path.Combine(dir, "MindAttic.Launcher.slnx"));
}
