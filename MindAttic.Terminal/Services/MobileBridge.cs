using System.Diagnostics;
using MindAttic.Terminal.Models;

namespace MindAttic.Terminal.Services;

/// <summary>
/// Bridge context — note the token is NOT included. Route() reads the token
/// from the Vault token bucket on demand so it never sits in a long-lived
/// struct that could be logged.
/// </summary>
public sealed record MobileBridgeContext(
    string HostExePath,
    string ServerUrl,
    string WorkingDirectory);

/// <summary>
/// Optional handoff to <c>MindAttic.Mobile.AgentHost.exe</c>. When enabled,
/// the agent's stdio is proxied to the Mobile server over SignalR so a
/// phone/iPad can drive the tab.
///
/// <para><b>KILL SWITCH:</b> <see cref="FeatureEnabled"/> is <c>false</c>
/// until <c>MindAttic.Mobile</c> ships. Flip it when Mobile is ready, after
/// which the existing settings.json gates take over.</para>
/// </summary>
public sealed class MobileBridge
{
    /// <summary>
    /// Master kill switch. Flip to <c>true</c> once MindAttic.Mobile is ready.
    /// Kept as <c>static readonly</c> (not <c>const</c>) so the compiler does
    /// not flag the gate-logic block below as unreachable.
    /// </summary>
    public static readonly bool FeatureEnabled = false;

    public static readonly string[] DefaultHostExeCandidates =
    [
        @"D:\Projects\MindAttic\MindAttic.Mobile\MindAttic.Mobile.AgentHost\bin\Release\net10.0\MindAttic.Mobile.AgentHost.exe",
        @"D:\Projects\MindAttic\MindAttic.Mobile\MindAttic.Mobile.AgentHost\bin\Debug\net10.0\MindAttic.Mobile.AgentHost.exe"
    ];

    private readonly IReadOnlyList<string> hostExeCandidates;
    private readonly Func<string, bool> exists;

    public MobileBridge() : this(DefaultHostExeCandidates, File.Exists) { }

    public MobileBridge(IReadOnlyList<string> hostExeCandidates, Func<string, bool> exists)
    {
        this.hostExeCandidates = hostExeCandidates;
        this.exists = exists;
    }

    /// <summary>
    /// Ports the gate logic from <c>Test-UseMobileBridge</c>. The Vault token
    /// is fetched directly via <paramref name="tokenSource"/> rather than via
    /// the in-memory <c>AppSettings.Mobile.Token</c> so it stays out of any
    /// struct a caller might log.
    /// </summary>
    public bool ShouldUse(AppSettings settings, Project project, SettingsStore tokenSource, out MobileBridgeContext? context)
    {
        context = null;

        if (!FeatureEnabled) return false;
        if (!settings.Mobile.Enabled) return false;
        if (project.MobileEnabled == false) return false;

        var hostExe = FindHostExe();
        if (hostExe is null) return false;

        var opted = settings.Mobile.AllProjects || project.MobileEnabled == true;
        if (!opted) return false;

        if (string.IsNullOrWhiteSpace(settings.Mobile.ServerUrl)) return false;
        if (string.IsNullOrWhiteSpace(tokenSource.GetMobileToken())) return false;

        var workingDir = Directory.Exists(project.Path) ? project.Path : Environment.CurrentDirectory;
        context = new MobileBridgeContext(hostExe, settings.Mobile.ServerUrl!, workingDir);
        return true;
    }

    /// <summary>
    /// Shells out to <c>MindAttic.Mobile.AgentHost.exe</c> with the agent run
    /// command + bridge args. The token is fetched from Vault here, used to
    /// build argv, and never stored elsewhere.
    /// </summary>
    public int Route(MobileBridgeContext context, SettingsStore tokenSource, AgentProvider provider, string projectName, string title)
    {
        var token = tokenSource.GetMobileToken()
                    ?? throw new InvalidOperationException("Mobile token is missing from the Vault token bucket.");

        var psi = new ProcessStartInfo(context.HostExePath)
        {
            UseShellExecute = false,
            WorkingDirectory = context.WorkingDirectory
        };
        foreach (var arg in new[]
        {
            "--provider", provider.Key,
            "--project",  projectName,
            "--title",    title,
            "--server",   context.ServerUrl,
            "--token",    token,
            "--run-command", provider.RunCommand,
            "--working-directory", context.WorkingDirectory
        })
        {
            psi.ArgumentList.Add(arg);
        }

        using var p = Process.Start(psi)
                      ?? throw new InvalidOperationException($"Failed to start {context.HostExePath}");
        p.WaitForExit();
        return p.ExitCode;
    }

    private string? FindHostExe()
    {
        foreach (var candidate in hostExeCandidates)
            if (exists(candidate)) return candidate;
        return null;
    }
}
