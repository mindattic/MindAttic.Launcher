using System.Diagnostics;
using System.Globalization;

namespace MindAttic.Launcher.Services;

public sealed record ScheduledTaskStatus(
    bool Registered, bool Enabled, DateTime? LastRunTime, int? LastTaskResult, DateTime? NextRunTime);

/// <summary>
/// Status/toggle for the nightly backup Scheduled Task, via <c>schtasks.exe</c> — consistent with
/// every other external tool this app execs (git/wt/robocopy/sqlcmd/sqlite3) rather than a new
/// Task Scheduler library dependency. Registration itself shells out to
/// <c>tools\register-nightly-backup-task.ps1</c>, which owns the fiddly S4U/trigger/principal
/// composition in one place instead of duplicating it in C#. Every task this app creates lives
/// under <c>\MindAttic\Launcher\</c>, per <c>MindAttic.HouseRules.md#HOUSE-LAW-11</c>.
/// </summary>
public sealed class ScheduledTaskService
{
    public const string TaskFolder = @"\MindAttic\Launcher\";
    public const string TaskName = "MindAtticLauncherNightlyBackup";

    public delegate (int ExitCode, string Output) ProcessRunner(string executable, IReadOnlyList<string> args, CancellationToken ct);

    private readonly ProcessRunner run;
    private readonly string registerScriptPath;

    public ScheduledTaskService() : this(DefaultRun, DefaultRegisterScriptPath()) { }

    public ScheduledTaskService(ProcessRunner runner, string registerScriptPath)
    {
        run = runner;
        this.registerScriptPath = registerScriptPath;
    }

    private static string DefaultRegisterScriptPath()
    {
        var root = ExePath.RepoRoot;
        return root is null
            ? Path.Combine(AppContext.BaseDirectory, "tools", "register-nightly-backup-task.ps1")
            : Path.Combine(root, "tools", "register-nightly-backup-task.ps1");
    }

    public static IReadOnlyList<string> BuildQueryArguments() =>
        ["/Query", "/TN", TaskFolder + TaskName, "/FO", "CSV", "/V", "/NH"];

    public static IReadOnlyList<string> BuildEnableArguments(bool enabled) =>
        ["/Change", "/TN", TaskFolder + TaskName, enabled ? "/Enable" : "/Disable"];

    public static IReadOnlyList<string> BuildRunNowArguments() =>
        ["/Run", "/TN", TaskFolder + TaskName];

    public static IReadOnlyList<string> BuildUnregisterArguments() =>
        ["/Delete", "/TN", TaskFolder + TaskName, "/F"];

    /// <summary>
    /// Parses one <c>schtasks /Query /FO CSV /V /NH</c> row. Verbose CSV columns (0-indexed):
    /// 1 TaskName, 3 Status, 5 Last Run Time, 6 Last Result, 8 Next Run Time... the layout Bob's
    /// own (pre-restore) sibling tools already relied on (see MindAttic.Psst's ScheduledTaskLister).
    /// Dates are locale-formatted, so parsing falls through to null rather than throwing.
    /// </summary>
    public static ScheduledTaskStatus ParseQueryCsv(string stdout)
    {
        foreach (var raw in stdout.Split('\n'))
        {
            var line = raw.Trim('\r').Trim();
            if (line.Length == 0) continue;

            var fields = ParseCsvLine(line);
            if (fields.Count < 7) continue;

            var status = fields[3];
            var enabled = !string.Equals(status, "Disabled", StringComparison.OrdinalIgnoreCase);

            DateTime? lastRun = DateTime.TryParse(fields[5], CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out var lr) ? lr : null;
            // schtasks reports this as an unsigned 32-bit value for some non-zero codes
            // (e.g. "task currently running"), which overflows Int32 — widen through long.
            int? lastResult = long.TryParse(fields[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out var code)
                ? unchecked((int)code)
                : null;
            DateTime? nextRun = DateTime.TryParse(fields[2], CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out var nr) ? nr : null;

            return new ScheduledTaskStatus(Registered: true, enabled, lastRun, lastResult, nextRun);
        }
        return new ScheduledTaskStatus(Registered: false, Enabled: false, null, null, null);
    }

    public ScheduledTaskStatus Query(CancellationToken ct = default)
    {
        var (code, output) = run("schtasks.exe", BuildQueryArguments(), ct);
        if (code != 0) return new ScheduledTaskStatus(Registered: false, Enabled: false, null, null, null);
        return ParseQueryCsv(output);
    }

    public (bool Ok, string Output) SetEnabled(bool enabled, CancellationToken ct = default)
    {
        var (code, output) = run("schtasks.exe", BuildEnableArguments(enabled), ct);
        return (code == 0, output);
    }

    public (bool Ok, string Output) RunNow(CancellationToken ct = default)
    {
        var (code, output) = run("schtasks.exe", BuildRunNowArguments(), ct);
        return (code == 0, output);
    }

    public (bool Ok, string Output) Unregister(CancellationToken ct = default)
    {
        var (code, output) = run("schtasks.exe", BuildUnregisterArguments(), ct);
        return (code == 0, output);
    }

    /// <summary>Registers (or re-registers) the nightly backup task via the companion .ps1 — see the class doc.</summary>
    public (bool Ok, string Output) Register(string time = "00:00", int keep = 30, CancellationToken ct = default)
    {
        if (!File.Exists(registerScriptPath))
            return (false, $"Registration script not found: {registerScriptPath}");

        var args = new[]
        {
            "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", registerScriptPath,
            "-Time", time, "-Keep", keep.ToString(CultureInfo.InvariantCulture)
        };
        var (code, output) = run("powershell.exe", args, ct);
        return (code == 0, output);
    }

    private static List<string> ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var sb = new System.Text.StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                    else inQuotes = false;
                }
                else sb.Append(c);
            }
            else
            {
                if (c == '"') inQuotes = true;
                else if (c == ',') { fields.Add(sb.ToString()); sb.Clear(); }
                else sb.Append(c);
            }
        }
        fields.Add(sb.ToString());
        return fields;
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
        try { p.WaitForExit(10_000); } catch { }
        try { Task.WaitAll([stdoutTask, stderrTask], TimeSpan.FromSeconds(10)); } catch { }

        int code;
        try { code = p.ExitCode; } catch { code = -1; }
        var stderr = stderrTask.IsCompletedSuccessfully ? stderrTask.Result : "";
        var stdout = stdoutTask.IsCompletedSuccessfully ? stdoutTask.Result : "";
        return (code, string.Join("\n", stdout, stderr).Trim());
    }
}
