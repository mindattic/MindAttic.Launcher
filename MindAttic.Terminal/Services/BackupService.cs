using System.Diagnostics;

namespace MindAttic.Terminal.Services;

public sealed record BackupResult(bool Ok, int RobocopyExitCode, TimeSpan Elapsed, string TargetFolder);

/// <summary>
/// robocopy-backed backup of <c>D:\Projects\MindAttic</c> →
/// <c>R:\Backup\MindAttic\&lt;yyyy-MM-dd&gt;[_a..z]</c>. Ports MindAttic.ps1
/// §"Backup" verbatim including the exclude lists and the dated-folder
/// allocator with letter-suffix collision handling.
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
        while (!p.HasExited)
        {
            if (ct.IsCancellationRequested)
            {
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

        Task.WaitAll(stdoutTask, stderrTask);
        sw.Stop();

        var code = p.ExitCode;
        return new BackupResult(code < 8, code, sw.Elapsed, targetFolder);
    }
}
