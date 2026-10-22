using System.Diagnostics;

namespace MindAttic.Launcher.Services;

/// <summary>
/// Shells out to the sibling <c>orchestrator</c> CLI (MindAttic.Orchestrator.Cli, deployed alongside
/// MindAttic.Orchestrator.exe and already on PATH per its deploy.ps1). Used by <see cref="ClaudeAccountCatalog"/>
/// for per-account usage and the Auto pick (M.LNCH-T53). <c>orchestrator</c> not being on PATH, erroring, or
/// timing out is not a Launcher failure — it just means no data, the same tolerance M.LNCH-T51 asked for
/// ("no change when the orchestrator isn't installed").
/// </summary>
public static class OrchestratorCli
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    /// <summary>Runs <c>orchestrator &lt;args&gt;</c> and returns its stdout, or null when it couldn't be
    /// started, exited non-zero, or didn't finish within <see cref="Timeout"/>.</summary>
    public static string? Run(params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo(ExecutableResolver.Resolve("orchestrator"))
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            foreach (var a in args) psi.ArgumentList.Add(a);

            using var p = Process.Start(psi);
            if (p is null) return null;
            var stdout = p.StandardOutput.ReadToEnd();
            p.StandardError.ReadToEnd();
            if (!p.WaitForExit((int)Timeout.TotalMilliseconds))
            {
                try { p.Kill(entireProcessTree: true); } catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
                return null;
            }
            return p.ExitCode == 0 ? stdout : null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            // Win32Exception covers "orchestrator isn't on PATH" — the common case when
            // MindAttic.Orchestrator isn't installed on this machine.
            return null;
        }
    }
}
