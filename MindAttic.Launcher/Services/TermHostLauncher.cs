using System.Diagnostics;
using System.Text;

namespace MindAttic.Launcher.Services;

/// <summary>
/// Opens a CLI inside an Orchestrator TermHost (M.LNCH-T82, M.ORCH-T922 phase 1), dashboard up or down
/// (M.LNCH-T83; an embedded Launcher sends its PaneRequest instead): a detached per-CLI process keeps it alive outside any
/// window, and the dashboard discovers it through the session registry. Nothing references the Orchestrator's packages: the install folder, the version-folder naming and the
/// <c>--launch</c> arguments mirror its <c>TermHostInstalls.ResolveExe</c> and <c>TermLauncher</c>.
/// </summary>
public static class TermHostLauncher
{
    public const string InstallDir = @"C:\Apps\MindAttic.Orchestrator";
    public const string HostPathVariable = "MINDATTIC_TERMHOST";
    public const string BillingVariable = "MINDATTIC_CLAUDE_BILLING";
    public const string FolderName = "TermHost";
    public const string ExeName = "MindAttic.Orchestrator.TermHost.exe";
    public const string StartedMessage = "Started in the background; it will appear in the Orchestrator dashboard.";

    /// <summary>The install folders to look in, in the Orchestrator's order: <c>C:\Apps</c>, then
    /// <c>%LOCALAPPDATA%\Programs\MindAttic.Orchestrator</c> (where its installer puts it without rights to C:\Apps).</summary>
    public static IReadOnlyList<string> InstallDirs()
    {
        var dirs = new List<string> { InstallDir };
        if (Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) is { Length: > 0 } local)
            dirs.Add(Path.Combine(local, "Programs", "MindAttic.Orchestrator"));
        return dirs;
    }

    /// <summary>The TermHost exe to start (M.LNCH-T85), as the Orchestrator's <c>TermLauncher.FindHost</c> finds it: the file
    /// <c>MINDATTIC_TERMHOST</c> names when it exists, else the newest install under each of <see cref="InstallDirs"/>; null when none.</summary>
    public static string? ResolveExe()
    {
        if (Environment.GetEnvironmentVariable(HostPathVariable) is { Length: > 0 } named && File.Exists(named.Trim()))
            return Path.GetFullPath(named.Trim());
        foreach (var dir in InstallDirs())
            if (ResolveExe(dir) is { } exe) return exe;
        return null;
    }

    /// <summary>The newest installed TermHost exe under <paramref name="installDir"/>\TermHost\&lt;version&gt;\, or null.
    /// Folders are named <c>major.minor.patch[-yyyyMMddHHmmss]</c>; the newest is by version, then stamp, then name.</summary>
    public static string? ResolveExe(string installDir)
    {
        var root = Path.Combine(installDir, FolderName);
        if (!Directory.Exists(root)) return null;
        try
        {
            return Directory.EnumerateDirectories(root)
                .Select(dir => (dir, name: Path.GetFileName(dir), parsed: ParseFolder(Path.GetFileName(dir))))
                .Where(x => x.parsed is not null && File.Exists(Path.Combine(x.dir, ExeName)))
                .OrderBy(x => x.parsed!.Value.Version).ThenBy(x => x.parsed!.Value.Stamp, StringComparer.Ordinal).ThenBy(x => x.name, StringComparer.Ordinal)
                .Select(x => Path.Combine(x.dir, ExeName))
                .LastOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    private static (Version Version, string Stamp)? ParseFolder(string name)
    {
        var dash = name.IndexOf('-');
        var versionText = dash < 0 ? name : name[..dash];
        var stamp = dash < 0 ? "" : name[(dash + 1)..];
        if (!Version.TryParse(versionText, out var version) || version.Major < 0 || version.Minor < 0) return null;
        if (stamp.Length > 0 && (stamp.Length != 14 || !stamp.All(char.IsAsciiDigit))) return null;
        return (version, stamp);
    }

    /// <summary>The <c>--launch</c> arguments: id, folder and title, then <c>--</c> and the CLI's whole command line as one
    /// argument (the host runs a single argument with a space in it as a whole command line). <paramref name="login"/> (the
    /// account email) and <paramref name="repo"/> go to the host's <c>--login</c>/<c>--repo</c> (M.ORCH-T925), which register the
    /// session for the dashboard under that person and repo; <paramref name="configDir"/> becomes the host's
    /// <c>CLAUDE_CONFIG_DIR</c> (<c>--env</c>), which its registration reads (M.LNCH-T84). Without an email, the login is read
    /// from the config dir's folder name, as the per-CLI logins are named by email.</summary>
    public static IReadOnlyList<string> Arguments(string id, string? directory, string? title, IReadOnlyList<string> command,
        string? login = null, string? repo = null, string? configDir = null)
    {
        var args = new List<string> { "--launch", "--id", id };
        if (!string.IsNullOrWhiteSpace(directory)) args.AddRange(["--cwd", directory]);
        if (!string.IsNullOrWhiteSpace(title)) args.AddRange(["--title", title]);
        if (!string.IsNullOrWhiteSpace(login)) args.AddRange(["--login", login]);
        else if (!string.IsNullOrWhiteSpace(configDir) && AccountScope.Normalize(Path.GetFileName(configDir.TrimEnd(['\\', '/']))) is { } folderLogin)
            args.AddRange(["--login", folderLogin]);
        if (!string.IsNullOrWhiteSpace(repo)) args.AddRange(["--repo", repo]);
        if (!string.IsNullOrWhiteSpace(configDir)) args.AddRange(["--env", "CLAUDE_CONFIG_DIR=" + configDir]);
        // The API key's own login (M.LNCH-T86), as the Orchestrator's PaneRequest.ApiKeySetup: mark the billing, and remove the
        // tokens that would bill a seat or a gateway instead (KEY= removes a variable), so the Vault key is what bills.
        if (ClaudeBillingMode.OfLogin(configDir) == ClaudeBillingMode.Api)
            args.AddRange(["--env", BillingVariable + "=api", "--env", "CLAUDE_CODE_OAUTH_TOKEN=", "--env", "ANTHROPIC_AUTH_TOKEN="]);
        args.AddRange(["--", CommandLine(command)]);
        return args;
    }

    /// <summary>Windows command line for <paramref name="command"/>, each argument quoted by the CreateProcess rules.</summary>
    public static string CommandLine(IReadOnlyList<string> command) => string.Join(' ', command.Select(Quote));

    private static string Quote(string arg)
    {
        if (arg.Length > 0 && !arg.Any(c => char.IsWhiteSpace(c) || c == '"')) return arg;
        var sb = new StringBuilder("\"");
        var backslashes = 0;
        foreach (var c in arg)
        {
            if (c == '\\') { backslashes++; continue; }
            if (c == '"') sb.Append('\\', backslashes * 2 + 1).Append('"');
            else sb.Append('\\', backslashes).Append(c);
            backslashes = 0;
        }
        return sb.Append('\\', backslashes * 2).Append('"').ToString();
    }

    /// <summary>Starts <paramref name="command"/> under a detached TermHost and says where it went. False, doing nothing,
    /// when no TermHost is installed or it fails to launch:
    /// the caller then opens its Windows Terminal window as before.</summary>
    public static bool TryStart(string? directory, string? title, IReadOnlyList<string> command, Action<string>? say = null,
        string? login = null, string? repo = null, string? configDir = null)
    {
        if (ResolveExe() is not { } exe) return false;
        try
        {
            var id = Guid.NewGuid().ToString("N")[..12];
            var psi = new ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                // The host's own folder, never the CLI's: the launch holds no handle on a repo.
                WorkingDirectory = Path.GetDirectoryName(exe) ?? Environment.CurrentDirectory,
            };
            foreach (var a in Arguments(id, directory, title, command, login, repo, configDir)) psi.ArgumentList.Add(a);
            // The host's CLI inherits this environment; the login rides in the command (--config-dir) and the token comes
            // from Vault in the host command, exactly as in a wt tab. A secret never goes on the command line.
            psi.Environment.Remove("NO_COLOR");

            using var launch = Process.Start(psi);
            if (launch is null) return false;
            _ = launch.StandardOutput.ReadToEndAsync();
            _ = launch.StandardError.ReadToEndAsync();
            if (!launch.WaitForExit(TimeSpan.FromSeconds(30)))
            {
                try { launch.Kill(); } catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
                return false;
            }
            if (launch.ExitCode != 0) return false;
            (say ?? Console.WriteLine)(StartedMessage);
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException) { return false; }
    }
}
