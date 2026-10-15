using System.Diagnostics;

namespace MindAttic.Launcher.Services;

public sealed record SqliteRestoreResult(
    string Project, string DatabaseFile, bool Ok, int ExitCode, TimeSpan Elapsed, string DumpFile, string Output = "");

/// <summary>
/// sqlite3-backed restore of a <c>.sql</c> dump produced by <see cref="SqliteBackupService"/>.
/// A dump is a full schema+data script (<c>.dump</c>'s output), so restoring just replays it into
/// a fresh file — any existing file at the destination is removed first rather than merged into,
/// since the dump already recreates every table from scratch. Same shape as
/// <see cref="SqliteBackupService"/>: an injectable runner so the process plumbing can be faked.
/// </summary>
public sealed class SqliteRestoreService
{
    /// <summary>Runs sqlite3 against <paramref name="dbFile"/> with <paramref name="sql"/> piped to stdin.</summary>
    public delegate (int ExitCode, string Output) SqliteRunner(string dbFile, string sql, CancellationToken ct);

    public string Executable { get; }
    private readonly SqliteRunner run;

    public SqliteRestoreService() : this("sqlite3") { }

    public SqliteRestoreService(string executable, SqliteRunner? runner = null)
    {
        Executable = executable;
        run = runner ?? DefaultRun;
    }

    public SqliteRestoreResult RestoreOne(string project, string dbFile, string dumpFile, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        if (!File.Exists(dumpFile))
        {
            sw.Stop();
            return new SqliteRestoreResult(project, dbFile, false, -1, sw.Elapsed, dumpFile, $"Dump file not found: {dumpFile}");
        }

        try
        {
            if (File.Exists(dbFile)) File.Delete(dbFile);
            var dir = Path.GetDirectoryName(dbFile);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var sql = File.ReadAllText(dumpFile);
            var (code, output) = run(dbFile, sql, ct);
            sw.Stop();

            var ok = code == 0 && !ct.IsCancellationRequested;
            if (!ok)
            {
                // A failed restore can leave a partially-applied file behind; remove it so it
                // doesn't masquerade as a successfully-restored database.
                try { if (File.Exists(dbFile)) File.Delete(dbFile); } catch { /* best-effort */ }
            }
            return new SqliteRestoreResult(project, dbFile, ok, code, sw.Elapsed, dumpFile, ok ? "" : Tail(output, 2000));
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new SqliteRestoreResult(project, dbFile, false, -1, sw.Elapsed, dumpFile, ex.Message);
        }
    }

    private (int, string) DefaultRun(string dbFile, string sql, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(Executable)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add(dbFile);

        using var p = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start {Executable}.");
        var stdoutTask = Task.Run(() => p.StandardOutput.ReadToEnd(), CancellationToken.None);
        var stderrTask = Task.Run(() => p.StandardError.ReadToEnd(), CancellationToken.None);
        p.StandardInput.Write(sql);
        p.StandardInput.Close();

        while (!p.HasExited)
        {
            if (ct.IsCancellationRequested)
            {
                try { p.Kill(entireProcessTree: true); } catch { }
                break;
            }
            p.WaitForExit(250);
        }

        try { p.WaitForExit(5000); } catch { }
        try { Task.WaitAll([stdoutTask, stderrTask], TimeSpan.FromSeconds(5)); } catch { }

        int code;
        try { code = p.ExitCode; }
        catch { code = -1; }

        var stderr = stderrTask.IsCompletedSuccessfully ? stderrTask.Result : "";
        var stdout = stdoutTask.IsCompletedSuccessfully ? stdoutTask.Result : "";
        return (code, string.Join("\n", stderr, stdout).Trim());
    }

    private static string Tail(string text, int maxChars)
    {
        if (string.IsNullOrEmpty(text)) return "";
        text = text.Trim();
        return text.Length <= maxChars ? text : text[^maxChars..];
    }
}
