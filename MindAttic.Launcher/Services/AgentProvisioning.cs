using System.Diagnostics;
using MindAttic.Vault.Paths;

namespace MindAttic.Launcher.Services;

/// <summary>
/// Best-effort, fire-and-forget provisioning of this workspace's shared CLI abilities/hooks
/// (<c>mindattic-agent-standard/render-abilities.ps1</c> + <c>install-hooks.ps1</c>, built by
/// MAT-T93/94/95/96 in MindAttic.Agency) right before <see cref="Commands.HostAgentCommand"/>
/// actually starts a provider's CLI (M.LNCH-T113). Closes the one gap those tickets left open: an
/// edit to <c>shared/abilities/*.md</c> or <c>shared/hooks/*.py</c> only reached Codex/Kimi/agy/Claude
/// once a human remembered to rerun both scripts by hand — "going forward any enhancement to any
/// CLI's abilities are shared amongst all compatible CLIs" is the user's own words for the ask.
/// <para>Gated by <see cref="IsStale"/> so an ordinary launch — nothing in <c>shared/abilities</c> or
/// <c>shared/hooks</c> changed since the last successful run — does no real work at all: it stats a
/// marker file and a handful of source files, then returns. When provisioning actually is due, both
/// scripts are run on a background thread and <see cref="EnsureProvisionedFireAndForget"/> returns
/// immediately without waiting on them. Timing an actual <c>-WhatIf</c> pass of each script
/// (2026-11-06, against the real workspace) took ~1.0s and ~0.4s respectively — together a stall a
/// user opening a tab would notice if this ran inline on every launch. Since staleness makes this rare
/// (only the first tab opened after an edit) and nothing about a fresh CLI session depends on today's
/// abilities/hooks being wired before its first prompt, there's no reason to make the user wait on it;
/// fire-and-forget keeps every launch's latency unchanged, always.</para>
/// <para>Every failure path here — scripts not found, powershell missing, a script erroring or
/// hanging — is caught and logged as a <see cref="Serilog.Log"/> warning (this app's own
/// crash/diagnostic log, wired once in <c>Program.cs</c>), never written to the CLI tab's own
/// console (which this runs alongside, not in place of). Nothing in this class ever throws out to a
/// caller, and a provisioning failure never prevents, delays, or otherwise touches the actual
/// launch.</para>
/// </summary>
public static class AgentProvisioning
{
    /// <summary>The mirrored, read-only-from-here copy of MindAttic.Agency's scripts/shared content
    /// that sits beside every project repo under <see cref="ExePath.WorkspaceRoot"/>.</summary>
    public const string ScriptsDirName = "mindattic-agent-standard";

    private const string RenderAbilitiesScript = "render-abilities.ps1";
    private const string InstallHooksScript = "install-hooks.ps1";

    /// <summary>The subdirectories whose newest file mtime decides staleness — exactly the content the
    /// two scripts render from.</summary>
    private static readonly string[] WatchedSubdirs = ["shared/abilities", "shared/hooks"];

    /// <summary>Where <see cref="EnsureProvisioned"/> records its last successful run. One marker for the
    /// whole workspace (not per-login/provider): the source content it watches is shared across every CLI,
    /// so there is nothing to gain from tracking staleness any more finely than that.</summary>
    public static string DefaultMarkerPath() =>
        Path.Combine(VaultPaths.LocalApp("MindAttic.Launcher"), "agent-provisioning.marker");

    /// <summary>Production entry point: starts <see cref="EnsureProvisioned"/> on a background thread and
    /// returns at once, deliberately not awaited by the caller — see the type doc for why inline/blocking
    /// isn't worth it. Call this, not <see cref="EnsureProvisioned"/> directly, from anywhere that launches
    /// a CLI.</summary>
    public static void EnsureProvisionedFireAndForget(string? workspaceRoot = null) =>
        _ = Task.Run(() => EnsureProvisioned(workspaceRoot));

    /// <summary>The synchronous, fully-testable core behind <see cref="EnsureProvisionedFireAndForget"/>.
    /// Never throws: every failure is caught, reported via <paramref name="warn"/> (default: a
    /// <see cref="Serilog.Log"/> warning), and swallowed so the launch that triggered this is never
    /// affected.</summary>
    public static void EnsureProvisioned(
        string? workspaceRoot = null,
        string? markerPath = null,
        Action<string>? warn = null,
        Func<string, (bool Ok, string? Error)>? runScript = null)
    {
        warn ??= static message => Serilog.Log.Warning("AgentProvisioning: {Message}", message);
        try
        {
            var scriptsDir = Path.Combine(workspaceRoot ?? ExePath.WorkspaceRoot, ScriptsDirName);
            // Not every checkout has the mirror sibling repo (e.g. a dev machine missing it) — quietly
            // skip rather than warn on every single launch for a condition that isn't going to change
            // until someone clones it.
            if (!Directory.Exists(scriptsDir)) return;

            markerPath ??= DefaultMarkerPath();
            if (!IsStale(scriptsDir, markerPath)) return;

            runScript ??= script => RunScript(Path.Combine(scriptsDir, script));

            var (renderOk, renderError) = runScript(RenderAbilitiesScript);
            if (!renderOk)
            {
                warn($"{RenderAbilitiesScript} failed; abilities/hooks stay as they were until the next successful run ({renderError}).");
                return;
            }

            var (hooksOk, hooksError) = runScript(InstallHooksScript);
            if (!hooksOk)
            {
                warn($"{InstallHooksScript} failed; abilities/hooks stay as they were until the next successful run ({hooksError}).");
                return;
            }

            WriteMarker(markerPath);
        }
        catch (Exception ex)
        {
            warn($"skipped provisioning ({ex.GetType().Name}: {ex.Message}).");
        }
    }

    /// <summary>True when a file under <c>shared/abilities</c> or <c>shared/hooks</c> (beneath
    /// <paramref name="scriptsDir"/>) is newer than <paramref name="markerPath"/>'s own last-write time, or
    /// the marker doesn't exist yet (first run ever, or after it was cleared).</summary>
    public static bool IsStale(string scriptsDir, string markerPath)
    {
        if (!File.Exists(markerPath)) return true;
        var markerTimeUtc = File.GetLastWriteTimeUtc(markerPath);

        foreach (var subdir in WatchedSubdirs)
        {
            var dir = Path.Combine(scriptsDir, subdir.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(dir)) continue;
            foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                if (File.GetLastWriteTimeUtc(file) > markerTimeUtc) return true;
        }
        return false;
    }

    private static void WriteMarker(string markerPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(markerPath)!);
        File.WriteAllText(markerPath, DateTime.UtcNow.ToString("O"));
    }

    /// <summary>Shells out to one script via <c>powershell -NoProfile -ExecutionPolicy Bypass -File</c> — the
    /// same invocation style <see cref="ExePath.EnsureFresh"/> already uses for <c>run.ps1</c> — capped at
    /// 30s so a wedged script can never hang the background task indefinitely.</summary>
    private static (bool Ok, string? Error) RunScript(string scriptPath)
    {
        if (!File.Exists(scriptPath)) return (false, $"not found: {scriptPath}");

        var psi = new ProcessStartInfo("powershell")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-ExecutionPolicy");
        psi.ArgumentList.Add("Bypass");
        psi.ArgumentList.Add("-File");
        psi.ArgumentList.Add(scriptPath);

        try
        {
            using var p = Process.Start(psi);
            if (p is null) return (false, "failed to start powershell");

            var drainOut = Task.Run(() => p.StandardOutput.ReadToEnd());
            var drainErr = Task.Run(() => p.StandardError.ReadToEnd());
            if (!p.WaitForExit((int)TimeSpan.FromSeconds(30).TotalMilliseconds))
            {
                try { p.Kill(entireProcessTree: true); }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
                return (false, "timed out after 30s");
            }
            try { Task.WaitAll([drainOut, drainErr], TimeSpan.FromSeconds(5)); }
            catch (AggregateException) { }

            return p.ExitCode == 0 ? (true, null) : (false, $"exit {p.ExitCode}: {Truncate(drainErr.Result)}");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return (false, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static string Truncate(string s) => s.Length <= 200 ? s : s[..200] + "...";
}
