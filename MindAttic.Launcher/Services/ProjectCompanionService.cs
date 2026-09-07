using System.Diagnostics;
using MindAttic.Launcher.Models;

namespace MindAttic.Launcher.Services;

/// <summary>
/// Ensures project-specific companion services (such as Prose.Hub for the Prose
/// project) are running before an agent session starts, across all agent providers
/// (Claude, Codex, Gemini, Kimi).
/// </summary>
public static class ProjectCompanionService
{
    public const string ProseHealthUrl = "http://127.0.0.1:5900/api/health";

    public static bool DefaultHealthChecker(string url)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(1000) };
            using var response = client.GetAsync(url).GetAwaiter().GetResult();
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Injectable health checker for unit testing.</summary>
    public static Func<string, bool> HealthChecker { get; set; } = DefaultHealthChecker;

    /// <summary>Injectable process launcher for unit testing.</summary>
    public static Action<string>? ProcessLauncher { get; set; }

    public static void ResetForTesting()
    {
        HealthChecker = DefaultHealthChecker;
        ProcessLauncher = null;
    }

    /// <summary>
    /// Checks and starts any companion services required by <paramref name="project"/> or
    /// <paramref name="workingDirectory"/> before the agent CLI executes.
    /// </summary>
    public static void EnsureStarted(Project? project, string workingDirectory)
    {
        var name = project?.Name
            ?? Path.GetFileName(workingDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        if (string.Equals(name, "Prose", StringComparison.OrdinalIgnoreCase))
        {
            EnsureProseHub(workingDirectory);
        }
    }

    private static void EnsureProseHub(string workingDirectory)
    {
        if (HealthChecker(ProseHealthUrl))
            return;

        Console.WriteLine("[launcher] Spinning up Prose.Hub...");

        if (ProcessLauncher != null)
        {
            ProcessLauncher(workingDirectory);
            return;
        }

        var hookScript = Path.Combine(workingDirectory, ".claude", "hooks", "start-prose-hub.ps1");
        var deployedExe = @"C:\Apps\Prose\Prose.Hub\Prose.Hub.exe";

        if (File.Exists(hookScript))
        {
            var psi = new ProcessStartInfo("powershell.exe")
            {
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{hookScript}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            try
            {
                using var p = Process.Start(psi);
                p?.WaitForExit(15000);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[launcher] Warning: failed to run start-prose-hub.ps1: {ex.Message}");
            }
        }
        else if (File.Exists(deployedExe))
        {
            var psi = new ProcessStartInfo(deployedExe)
            {
                WorkingDirectory = Path.GetDirectoryName(deployedExe)!,
                UseShellExecute = true
            };
            psi.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
            try
            {
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[launcher] Warning: failed to launch Prose.Hub.exe: {ex.Message}");
            }
        }

        for (var i = 0; i < 10; i++)
        {
            Thread.Sleep(500);
            if (HealthChecker(ProseHealthUrl))
            {
                Console.WriteLine("[launcher] Prose.Hub is ready.");
                return;
            }
        }

        Console.WriteLine("[launcher] Prose.Hub start requested (proceeding with agent launch).");
    }
}
