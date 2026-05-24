using System.Diagnostics;

namespace MindAttic.Terminal.Services;

/// <summary>
/// Resolves paths to the running and canonical MindAttic.Terminal executables.
/// <see cref="Self"/> is whatever exe is currently executing (e.g. the Debug
/// bin during dev). <see cref="Release"/> is the published single-file exe at
/// <c>artifacts\MindAttic.Terminal.exe</c> — what the launcher .bat invokes
/// and what "Restart" respawns. <see cref="EnsureFresh"/> shells out to
/// <c>scripts\ensure-fresh.ps1</c> to republish when source has changed.
/// </summary>
public static class ExePath
{
    public static string Self => Environment.ProcessPath ?? "MindAttic.Terminal";

    public static string Release
    {
        get
        {
            var root = FindRepoRoot();
            return root is null
                ? Self
                : Path.Combine(root, "artifacts", "MindAttic.Terminal.exe");
        }
    }

    public static void EnsureFresh()
    {
        var root = FindRepoRoot();
        if (root is null) return;
        var script = Path.Combine(root, "scripts", "ensure-fresh.ps1");
        if (!File.Exists(script)) return;

        var psi = new ProcessStartInfo("powershell")
        {
            UseShellExecute = false,
            WorkingDirectory = root
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-ExecutionPolicy");
        psi.ArgumentList.Add("Bypass");
        psi.ArgumentList.Add("-File");
        psi.ArgumentList.Add(script);

        try
        {
            using var p = Process.Start(psi);
            p?.WaitForExit();
        }
        catch
        {
            // Best-effort republish — if powershell is missing or the script
            // fails, the caller still tries to launch whatever exe exists.
        }
    }

    private static string? FindRepoRoot()
    {
        var dir = Path.GetDirectoryName(Environment.ProcessPath ?? string.Empty);
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "scripts", "publish.ps1")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }
}
