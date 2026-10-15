using System.Diagnostics;

namespace MindAttic.Launcher.Services;

public sealed record PrerequisiteCheckResult(string Command, bool Present, bool Installed, string? Error);

/// <summary>An optional dev tool bootstrap can offer to install — never required by the pipeline.</summary>
public sealed record OptionalToolSpec(string Name, string CommandOnPath, string WingetId);

/// <summary>
/// Installs what <c>bootstrap</c> needs via <c>winget</c> — consistent with this app's everything
/// -is-an-exec'd-process ethos. By the time <c>bootstrap</c> runs, Launcher itself already proves
/// git and the .NET runtime are present (the seed script, <c>bootstrap.ps1</c>, handles that one
/// chicken-and-egg step), so this only covers what Bob calls its own hard requirements beyond
/// that — <c>sqlcmd</c> and SQL Server LocalDB — plus the same optional dev tools Bob offers.
/// No offline installer cache: that existed only to support the no-network bug-out scenario,
/// which no longer exists now that every repo lives on GitHub.
/// </summary>
public sealed class PrerequisiteService
{
    public delegate bool CommandExistsChecker(string command);
    public delegate (int ExitCode, string Output) ProcessRunner(string executable, IReadOnlyList<string> args, CancellationToken ct);

    /// <summary>(command on PATH, winget ids tried in order).</summary>
    public static readonly IReadOnlyList<(string Command, string[] WingetIds)> HardRequirements =
    [
        ("sqlcmd", ["Microsoft.sqlcmd"]),
        ("sqllocaldb", ["Microsoft.SQLServer.2022.LocalDB", "Microsoft.SQLServer.2019.LocalDB"]),
    ];

    public static readonly IReadOnlyList<OptionalToolSpec> OptionalTools =
    [
        new("Notepad++", "notepad++", "Notepad++.Notepad++"),
        new("Node.js and npm", "npm", "OpenJS.NodeJS.LTS"),
        new("VS Code", "code", "Microsoft.VisualStudioCode"),
        new("Visual Studio Community", "devenv", "Microsoft.VisualStudio.2022.Community"),
        new("SQL Server Management Studio", "ssms", "Microsoft.SQLServerManagementStudio"),
        new("Claude desktop", "claude", "Anthropic.Claude"),
        new("Antigravity IDE", "agy", "Google.AntigravityIDE"),
    ];

    private readonly CommandExistsChecker exists;
    private readonly ProcessRunner run;

    public PrerequisiteService() : this(DefaultCommandExists, DefaultRun) { }

    public PrerequisiteService(CommandExistsChecker exists, ProcessRunner run)
    {
        this.exists = exists;
        this.run = run;
    }

    public static IReadOnlyList<string> BuildWingetInstallArguments(string wingetId) =>
        ["install", "--id", wingetId, "-e", "--silent", "--accept-package-agreements", "--accept-source-agreements"];

    public bool CommandExists(string command) => exists(command);

    public (bool Ok, string Output) InstallViaWinget(string wingetId, CancellationToken ct = default)
    {
        var (code, output) = run("winget", BuildWingetInstallArguments(wingetId), ct);
        return (code == 0, output);
    }

    /// <summary>
    /// Ensures one hard requirement is on PATH, trying each winget id in order until one
    /// succeeds. Never blocks the caller with a UAC prompt itself — winget raises its own if the
    /// specific package needs it, which is the only elevation <c>bootstrap</c> ever triggers.
    /// </summary>
    public PrerequisiteCheckResult EnsureHard(string command, IReadOnlyList<string> wingetIds, CancellationToken ct = default)
    {
        if (exists(command)) return new PrerequisiteCheckResult(command, Present: true, Installed: false, null);

        string? lastError = null;
        foreach (var id in wingetIds)
        {
            var (ok, output) = InstallViaWinget(id, ct);
            if (ok || exists(command)) return new PrerequisiteCheckResult(command, Present: true, Installed: true, null);
            lastError = output;
        }
        return new PrerequisiteCheckResult(command, Present: false, Installed: false,
            lastError is null ? $"'{command}' not found and no winget id succeeded." : $"'{command}' not found: {lastError}");
    }

    private static bool DefaultCommandExists(string command)
    {
        try
        {
            var psi = new ProcessStartInfo("where")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add(command);
            using var p = Process.Start(psi);
            if (p is null) return false;
            p.WaitForExit(5000);
            return p.ExitCode == 0;
        }
        catch { return false; }
    }

    private static (int, string) DefaultRun(string executable, IReadOnlyList<string> args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var p = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start {executable}.");
        var stdoutTask = Task.Run(() => p.StandardOutput.ReadToEnd(), CancellationToken.None);
        var stderrTask = Task.Run(() => p.StandardError.ReadToEnd(), CancellationToken.None);

        while (!p.HasExited)
        {
            if (ct.IsCancellationRequested) { try { p.Kill(entireProcessTree: true); } catch { } break; }
            p.WaitForExit(250);
        }
        // winget can take a while for a large package (e.g. Visual Studio) — give it room.
        try { p.WaitForExit(600_000); } catch { }
        try { Task.WaitAll([stdoutTask, stderrTask], TimeSpan.FromSeconds(10)); } catch { }

        int code;
        try { code = p.ExitCode; } catch { code = -1; }
        var stderr = stderrTask.IsCompletedSuccessfully ? stderrTask.Result : "";
        var stdout = stdoutTask.IsCompletedSuccessfully ? stdoutTask.Result : "";
        return (code, string.Join("\n", stdout, stderr).Trim());
    }
}
