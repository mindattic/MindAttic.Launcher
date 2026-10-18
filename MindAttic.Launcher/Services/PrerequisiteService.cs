using System.Diagnostics;

namespace MindAttic.Launcher.Services;

public sealed record PrerequisiteCheckResult(string Command, bool Present, bool Installed, string? Error);

/// <summary>
/// An optional dev tool bootstrap can offer to install — never required by the pipeline.
/// <paramref name="CommandOnPath"/> is only set for a tool that really puts its own command on PATH;
/// a GUI app that doesn't (Claude desktop, Antigravity IDE, Visual Studio, SSMS) is found through
/// <paramref name="InstallPaths"/> (environment variables expanded) or, failing that, by asking
/// winget whether its package is installed.
/// </summary>
public sealed record OptionalToolSpec(string Name, string? CommandOnPath, string WingetId, IReadOnlyList<string>? InstallPaths = null);

/// <summary>
/// An AI agent CLI the Launcher's tabs run. Installed by the vendor's own PowerShell install script
/// when <paramref name="InstallScriptUrl"/> is set, else from npm when <paramref name="NpmPackage"/>
/// is set, otherwise from winget — the npm/winget split Bob used.
/// </summary>
public sealed record AiCliSpec(string Name, string Command, string? NpmPackage, string? WingetId, string? InstallScriptUrl = null);

/// <summary>
/// Installs what <c>bootstrap</c> needs via <c>winget</c> — consistent with this app's everything
/// -is-an-exec'd-process ethos. By the time <c>bootstrap</c> runs, Launcher itself already proves
/// git and the .NET runtime are present (the seed script, <c>setup.ps1</c>, handles that one
/// chicken-and-egg step), so this only covers what Bob calls its own hard requirements beyond
/// that — <c>sqlcmd</c> and SQL Server LocalDB (which it also creates/starts) — the AI agent CLIs
/// the tabs run, plus the same optional dev tools Bob offers.
/// No offline installer cache: that existed only to support the no-network bug-out scenario,
/// which no longer exists now that every repo lives on GitHub.
/// </summary>
public sealed class PrerequisiteService
{
    public delegate bool CommandExistsChecker(string command);
    public delegate (int ExitCode, string Output) ProcessRunner(string executable, IReadOnlyList<string> args, CancellationToken ct);

    /// <summary>The LocalDB instance bootstrap restores into unless told otherwise (Bob's <c>-SqlServer</c> default).</summary>
    public const string DefaultLocalDbInstance = "MSSQLLocalDB";

    /// <summary>Server name for <see cref="DefaultLocalDbInstance"/>, as sqlcmd addresses it.</summary>
    public const string DefaultLocalDbServer = @"(localdb)\" + DefaultLocalDbInstance;

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
        // Neither devenv nor ssms is on PATH outside a Developer prompt, so winget answers for them.
        new("Visual Studio Community", null, "Microsoft.VisualStudio.2022.Community"),
        new("SQL Server Management Studio", null, "Microsoft.SQLServerManagementStudio"),
        // 'claude' on PATH is the Claude Code CLI, not the desktop app; 'agy' is the Antigravity CLI, not the IDE.
        new("Claude desktop", null, "Anthropic.Claude",
            [@"%LOCALAPPDATA%\AnthropicClaude\claude.exe", @"%LOCALAPPDATA%\Programs\AnthropicClaude\Claude.exe"]),
        new("Antigravity IDE", null, "Google.AntigravityIDE",
            [@"%LOCALAPPDATA%\Programs\Antigravity\Antigravity.exe"]),
    ];

    /// <summary>The CLIs <see cref="AgentProviderRegistry.Defaults"/> run, installed the way Bob installed them.</summary>
    public static readonly IReadOnlyList<AiCliSpec> AiClis =
    [
        new("Claude Code CLI", "claude", "@anthropic-ai/claude-code", null),
        new("OpenAI Codex CLI", "codex", "@openai/codex", null),
        new("Antigravity CLI", "agy", null, "Google.AntigravityCLI"),
        // Moonshot's documented Windows install (kimi.com Kimi Code CLI getting-started): a single
        // kimi.exe in %USERPROFILE%\.kimi-code\bin, checksum-verified, bin added to the user PATH.
        // No Node needed, no prompts on success.
        new("Kimi Code CLI", "kimi", null, null, KimiInstallScriptUrl),
    ];

    /// <summary>Moonshot's official Kimi Code CLI installer for Windows.</summary>
    public const string KimiInstallScriptUrl = "https://code.kimi.com/kimi-code/install.ps1";

    /// <summary>winget package that provides npm, installed on demand when an npm-based CLI is missing.</summary>
    public const string NodeWingetId = "OpenJS.NodeJS.LTS";

    private readonly CommandExistsChecker exists;
    private readonly ProcessRunner run;
    private readonly Action refreshPath;
    private readonly Func<string, bool> fileExists;

    public PrerequisiteService() : this(DefaultCommandExists, DefaultRun, RefreshProcessPath, File.Exists) { }

    /// <summary>
    /// Test seam. <paramref name="refreshPath"/> defaults to a no-op and <paramref name="fileExists"/> to
    /// "nothing exists", so a test never touches the real environment or disk.
    /// </summary>
    public PrerequisiteService(CommandExistsChecker exists, ProcessRunner run, Action? refreshPath = null, Func<string, bool>? fileExists = null)
    {
        this.exists = exists;
        this.run = run;
        this.refreshPath = refreshPath ?? (() => { });
        this.fileExists = fileExists ?? (_ => false);
    }

    public static IReadOnlyList<string> BuildWingetInstallArguments(string wingetId) =>
        ["install", "--id", wingetId, "-e", "--silent", "--accept-package-agreements", "--accept-source-agreements"];

    public static IReadOnlyList<string> BuildWingetListArguments(string wingetId) =>
        ["list", "--id", wingetId, "-e", "--accept-source-agreements"];

    public static IReadOnlyList<string> BuildNpmInstallArguments(string package) => ["install", "-g", package];

    /// <summary>
    /// Runs a vendor's <c>irm &lt;url&gt; | iex</c> installer. -NonInteractive matters: Kimi's script ends
    /// a failed install with <c>Read-Host "Press Enter to exit"</c>, which then throws (and the script
    /// catches it and exits 1) instead of waiting forever on a console nobody is watching.
    /// </summary>
    public static IReadOnlyList<string> BuildInstallScriptArguments(string url) =>
        ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", $"irm '{url}' | iex"];

    /// <summary>
    /// The PATH a fresh shell would see (machine, then user — what an installer just changed),
    /// followed by any entry only this process had, so nothing the caller added is lost.
    /// </summary>
    public static string MergePath(string? machine, string? user, string? current)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var merged = new List<string>();
        foreach (var source in new[] { machine, user, current })
        {
            if (string.IsNullOrEmpty(source)) continue;
            foreach (var raw in source.Split(';'))
            {
                var entry = raw.Trim();
                if (entry.Length == 0) continue;
                if (seen.Add(entry.TrimEnd('\\'))) merged.Add(entry);
            }
        }
        return string.Join(';', merged);
    }

    /// <summary>
    /// Re-reads PATH from the registry into this process, as Bob's <c>Update-SessionPath</c> does after
    /// every install: winget changes the machine/user PATH, not ours, so without this a tool it just
    /// installed (sqlcmd, sqllocaldb, npm) isn't found by the very next step.
    /// </summary>
    public static void RefreshProcessPath()
    {
        var merged = MergePath(
            Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Machine),
            Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User),
            Environment.GetEnvironmentVariable("Path"));
        if (merged.Length > 0) Environment.SetEnvironmentVariable("Path", merged);
    }

    public bool CommandExists(string command) => exists(command);

    /// <summary>Installs a winget package and, on success, refreshes this process's PATH.</summary>
    public (bool Ok, string Output) InstallViaWinget(string wingetId, CancellationToken ct = default)
    {
        var (code, output) = run("winget", BuildWingetInstallArguments(wingetId), ct);
        if (code == 0) refreshPath();
        return (code == 0, output);
    }

    /// <summary>
    /// Whether an optional tool is already installed: its own command on PATH, one of its known
    /// install paths, or winget reporting its package as installed.
    /// </summary>
    public bool IsInstalled(OptionalToolSpec tool, CancellationToken ct = default)
    {
        if (tool.CommandOnPath is { Length: > 0 } cmd && exists(cmd)) return true;
        foreach (var path in tool.InstallPaths ?? [])
            if (fileExists(Environment.ExpandEnvironmentVariables(path))) return true;
        try
        {
            var (code, _) = run("winget", BuildWingetListArguments(tool.WingetId), ct);
            return code == 0;
        }
        catch { return false; }
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

    /// <summary>
    /// Ensures an AI CLI is on PATH: the vendor's install script when it has one, npm for an npm
    /// package (installing Node.js first via winget when npm itself is missing, as Bob's
    /// prerequisites did), winget otherwise. A failure is
    /// reported, never fatal — the pipeline itself doesn't need these.
    /// </summary>
    public PrerequisiteCheckResult EnsureAiCli(AiCliSpec cli, CancellationToken ct = default)
    {
        if (exists(cli.Command)) return new PrerequisiteCheckResult(cli.Command, Present: true, Installed: false, null);
        try { return InstallAiCli(cli, ct); }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return new PrerequisiteCheckResult(cli.Command, false, false, $"'{cli.Command}' not installed: {ex.Message}");
        }
    }

    private PrerequisiteCheckResult InstallAiCli(AiCliSpec cli, CancellationToken ct)
    {
        string? error;
        if (cli.InstallScriptUrl is { Length: > 0 } url)
        {
            // The script adds its bin folder to the user PATH; re-read it so 'kimi' is found below.
            var (code, output) = run("powershell.exe", BuildInstallScriptArguments(url), ct);
            if (code == 0) refreshPath();
            error = code == 0 ? null : output;
        }
        else if (cli.NpmPackage is { Length: > 0 } package)
        {
            if (!exists("npm"))
            {
                var (nodeOk, nodeOut) = InstallViaWinget(NodeWingetId, ct);
                if (!nodeOk && !exists("npm"))
                    return new PrerequisiteCheckResult(cli.Command, false, false,
                        $"npm isn't available and Node.js couldn't be installed ({nodeOut}); install Node, then 'npm install -g {package}'.");
            }

            // npm is npm.cmd on Windows; Process.Start only resolves .exe from a bare name.
            var (code, output) = run("npm.cmd", BuildNpmInstallArguments(package), ct);
            if (code == 0) refreshPath();
            error = code == 0 ? null : output;
        }
        else if (cli.WingetId is { Length: > 0 } id)
        {
            var (ok, output) = InstallViaWinget(id, ct);
            error = ok ? null : output;
        }
        else
        {
            error = "no install source";
        }

        return exists(cli.Command) || error is null
            ? new PrerequisiteCheckResult(cli.Command, Present: true, Installed: true, null)
            : new PrerequisiteCheckResult(cli.Command, Present: false, Installed: false, $"'{cli.Command}' not installed: {error}");
    }

    /// <summary>
    /// The LocalDB instance name in a <c>(localdb)\Instance</c> server name, or null for any other server.
    /// </summary>
    public static string? LocalDbInstanceOf(string? server)
    {
        const string prefix = @"(localdb)\";
        if (server is null || !server.Trim().StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        var instance = server.Trim()[prefix.Length..].Trim();
        return instance.Length == 0 ? null : instance;
    }

    /// <summary>
    /// Starts a LocalDB instance, creating it first when it doesn't exist yet — Bob's
    /// <c>Start-LocalDb</c>. A fresh LocalDB install has no instance running, so without this every
    /// <c>.bak</c> restore into <c>(localdb)\MSSQLLocalDB</c> fails to connect.
    /// </summary>
    public (bool Ok, string Message) EnsureLocalDbRunning(string instance, CancellationToken ct = default)
    {
        try { return StartOrCreateLocalDb(instance, ct); }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return (false, $"couldn't run sqllocaldb: {ex.Message}");
        }
    }

    private (bool Ok, string Message) StartOrCreateLocalDb(string instance, CancellationToken ct)
    {
        var (startCode, startOut) = run("sqllocaldb", ["start", instance], ct);
        if (startCode == 0) return (true, "running");

        var (createCode, createOut) = run("sqllocaldb", ["create", instance, "-s"], ct);
        if (createCode == 0) return (true, "created and started");

        var detail = string.Join("\n", startOut, createOut).Trim();
        return (false, detail.Length == 0 ? $"sqllocaldb couldn't start or create '{instance}'" : detail);
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
