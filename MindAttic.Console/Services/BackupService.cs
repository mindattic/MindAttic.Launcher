using System.Diagnostics;

namespace MindAttic.Console.Services;

public sealed record BackupResult(bool Ok, int RobocopyExitCode, TimeSpan Elapsed, string TargetFolder, string Output = "");

/// <summary>
/// robocopy-backed backup of <c>D:\Projects\MindAttic</c> →
/// <c>R:\Backup\MindAttic\&lt;yyyy-MM-dd&gt;[_a..z]</c>. Includes the exclude
/// lists and the dated-folder allocator with letter-suffix collision handling.
/// </summary>
public sealed class BackupService
{
    public const string DefaultSource     = @"D:\Projects\MindAttic";
    public const string DefaultBackupBase = @"R:\Backup\MindAttic";

    public static readonly string[] ExcludeDirs =
    [
        "Library", "Temp", "Logs", "obj", "bin", "Build", "Builds",
        "node_modules", ".vs", ".idea", ".git"
    ];

    public static readonly string[] ExcludeFiles = ["*.log", "*.tmp"];

    public string Source { get; }
    public string BackupBase { get; }
    public Func<string, bool> Exists { get; }
    public Func<DateTime> Now { get; }

    public BackupService() : this(DefaultSource, DefaultBackupBase) { }

    public BackupService(string source, string backupBase, Func<string, bool>? exists = null, Func<DateTime>? now = null)
    {
        Source = source;
        BackupBase = backupBase;
        Exists = exists ?? Directory.Exists;
        Now = now ?? (() => DateTime.Now);
    }

    /// <summary>
    /// Picks <c>{base}\{date}</c> if it doesn't exist, otherwise the first of
    /// <c>{base}\{date}_a..{base}\{date}_z</c> that's free. Pure — uses the
    /// injected Exists predicate so tests don't touch the filesystem.
    /// </summary>
    public string ResolveTargetFolder()
    {
        var date = Now().ToString("yyyy-MM-dd");
        var baseFolder = Path.Combine(BackupBase, date);
        if (!Exists(baseFolder)) return baseFolder;

        for (var c = 'a'; c <= 'z'; c++)
        {
            var candidate = $"{baseFolder}_{c}";
            if (!Exists(candidate)) return candidate;
        }

        throw new InvalidOperationException(
            $"All 27 dated folders for {date} already exist under {BackupBase}.");
    }

    /// <summary>
    /// Synchronous robocopy invocation. <paramref name="onTick"/> is called
    /// once with the current elapsed TimeSpan whenever the caller wants a
    /// progress refresh — wire it from a Spectre Status loop.
    /// </summary>
    public BackupResult Run(string targetFolder, Action<TimeSpan>? onTick = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(targetFolder);

        var args = new List<string> { Source, targetFolder, "/E", "/NFL", "/NDL", "/NJH", "/NJS", "/NP" };
        if (ExcludeDirs.Length > 0)
        {
            args.Add("/XD");
            args.AddRange(ExcludeDirs);
        }
        if (ExcludeFiles.Length > 0)
        {
            args.Add("/XF");
            args.AddRange(ExcludeFiles);
        }

        var psi = new ProcessStartInfo("robocopy")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        var sw = Stopwatch.StartNew();
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start robocopy.");
        var stdoutTask = Task.Run(() => p.StandardOutput.ReadToEnd(), CancellationToken.None);
        var stderrTask = Task.Run(() => p.StandardError.ReadToEnd(), CancellationToken.None);

        var lastTickSec = -1;
        var cancelled = false;
        while (!p.HasExited)
        {
            if (ct.IsCancellationRequested)
            {
                cancelled = true;
                try { p.Kill(entireProcessTree: true); } catch { }
                break;
            }
            p.WaitForExit(250);

            // Fire onTick at most once per second so we don't redraw the
            // status label faster than the spinner can repaint.
            var elapsedSec = (int)sw.Elapsed.TotalSeconds;
            if (onTick is not null && elapsedSec != lastTickSec)
            {
                lastTickSec = elapsedSec;
                onTick(sw.Elapsed);
            }
        }

        // Kill() is asynchronous, so after a cancellation break the process may
        // not have exited yet; wait (bounded) before reading ExitCode so we
        // don't throw "process has not exited" or read a stale code.
        try { p.WaitForExit(5000); } catch { }
        // Cap the drain wait so a wedged stream after Kill() can't hang the
        // menu indefinitely — same defensive posture as GitService.Run.
        try { Task.WaitAll(new[] { stdoutTask, stderrTask }, TimeSpan.FromSeconds(5)); } catch { }
        sw.Stop();

        // ExitCode throws if the process somehow still hasn't exited; treat that
        // (and any negative/kill code from cancellation) as a failure rather
        // than letting "< 8" report a cancelled or wedged run as a success.
        int code;
        try { code = p.ExitCode; }
        catch { code = -1; }
        var ok = ComputeOk(code, cancelled);
        // Robocopy writes most failure detail to stdout (per-file errors) and
        // some to stderr. Keep both, prefer stderr first, and trim to a tail
        // so we don't dump megabytes of output into the menu. If the drain
        // wait timed out, fall back to "" for the unfinished task so we don't
        // block on .Result.
        var stderrText = stderrTask.IsCompletedSuccessfully ? stderrTask.Result : "";
        var stdoutText = stdoutTask.IsCompletedSuccessfully ? stdoutTask.Result : "";
        var output = ok ? "" : Tail(string.Join("\n", stderrText, stdoutText), 2000);
        return new BackupResult(ok, code, sw.Elapsed, targetFolder, output);
    }

    /// <summary>
    /// robocopy exit codes 0–7 are success (files copied / nothing to do / minor
    /// housekeeping); 8+ are failures. A negative code only shows up when we
    /// killed the process (cancellation, wedge), so it — like an explicit
    /// cancellation — counts as a failure rather than a success.
    /// </summary>
    public static bool ComputeOk(int robocopyExitCode, bool cancelled) =>
        !cancelled && robocopyExitCode is >= 0 and < 8;

    private static string Tail(string text, int maxChars)
    {
        if (string.IsNullOrEmpty(text)) return "";
        text = text.Trim();
        return text.Length <= maxChars ? text : text[^maxChars..];
    }
}
