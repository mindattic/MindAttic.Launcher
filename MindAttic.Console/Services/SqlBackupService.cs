using System.Diagnostics;
using MindAttic.Console.Models;

namespace MindAttic.Console.Services;

/// <summary>One database's <c>BACKUP DATABASE</c> outcome.</summary>
public sealed record DatabaseBackupResult(
    string Server, string Database, bool Ok, int ExitCode, TimeSpan Elapsed, string BackupFile, string Output = "");

/// <summary>A (instance, database) pair to back up. Value-equal so duplicates dedupe.</summary>
public readonly record struct BackupTarget(string Server, string Database);

/// <summary>
/// sqlcmd-backed full backup (schema + data) of each project's SQL Server
/// databases into <c>{datedFolder}\Databases\{db}.bak</c>. A file-only robocopy
/// snapshot isn't a real backup of a live database — this runs alongside
/// <see cref="BackupService"/> so the dated folder also captures every
/// <c>BACKUP DATABASE</c>. Mirrors BackupService/GitService: pure helpers for
/// path/SQL/arg/exit-code logic (so tests don't touch a server) and an injectable
/// runner so the process plumbing can be faked.
/// </summary>
public sealed class SqlBackupService
{
    public const string DefaultInstance = "localhost";
    public const string DatabasesSubfolder = "Databases";

    /// <summary>Runs sqlcmd with the given argument list; returns its exit code and combined output.</summary>
    public delegate (int ExitCode, string Output) SqlcmdRunner(IReadOnlyList<string> args, CancellationToken ct);

    public string Executable { get; }
    private readonly SqlcmdRunner run;

    public SqlBackupService() : this("sqlcmd") { }

    public SqlBackupService(string executable, SqlcmdRunner? runner = null)
    {
        Executable = executable;
        run = runner ?? DefaultRun;
    }

    /// <summary>
    /// Flattens the roster into the distinct (instance, database) pairs to back
    /// up: every non-blank name in each project's <see cref="Project.Databases"/>,
    /// keyed to that project's <see cref="Project.SqlServer"/> (or the default
    /// instance). Two projects naming the same db on the same instance collapse
    /// to one backup.
    /// </summary>
    public static IReadOnlyList<BackupTarget> CollectTargets(AppSettings settings)
    {
        // Dedup case-insensitively on (server, database): SQL Server identifiers
        // and Windows file paths are both case-insensitive, so "MyDb" and "mydb"
        // on the same instance are one database — backing both up would just run
        // the second over the first's .bak. The NUL separator keeps a server that
        // ends in a db-like suffix from colliding with the next pair.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var targets = new List<BackupTarget>();
        foreach (var p in ProjectRoster.Sorted(settings))
            foreach (var raw in p.Databases ?? [])
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                var server = ResolveInstance(p.SqlServer);
                var database = raw.Trim();
                if (seen.Add($"{server}\u0000{database}"))
                    targets.Add(new BackupTarget(server, database));
            }
        return targets;
    }

    public static string ResolveInstance(string? instance) =>
        string.IsNullOrWhiteSpace(instance) ? DefaultInstance : instance.Trim();

    /// <summary>
    /// Maps a (server, database) pair to its .bak path under the dated folder,
    /// scrubbing path-illegal chars from both. The server is part of the path so
    /// the same database name on two different instances (e.g. <c>App</c> on
    /// <c>localhost</c> and on <c>.\SQLEXPRESS</c>) lands in distinct files
    /// instead of the second silently overwriting the first.
    /// </summary>
    public static string ResolveBackupFilePath(string targetFolder, string server, string database) =>
        Path.Combine(targetFolder, DatabasesSubfolder, SanitizeFileName(server), SanitizeFileName(database) + ".bak");

    private static string SanitizeFileName(string database)
    {
        var name = database.Trim();
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return string.IsNullOrEmpty(name) ? "database" : name;
    }

    /// <summary>
    /// Full backup T-SQL: <c>BACKUP DATABASE</c> captures schema and data in one
    /// .bak. COPY_ONLY so we don't disturb the differential/log chain; FORMAT+INIT
    /// overwrite any stale file from a re-run. The db name is bracket-escaped
    /// (<c>]</c>→<c>]]</c>) and the disk path single-quote-escaped — these are
    /// trusted local config values, but escaping keeps an odd name from breaking
    /// the statement. COMPRESSION is intentionally omitted (unsupported on Express).
    /// </summary>
    public static string BuildBackupSql(string database, string backupFile)
    {
        var ident = "[" + database.Replace("]", "]]") + "]";
        var disk = backupFile.Replace("'", "''");
        // CHECKSUM has the engine checksum every page as it writes, so a torn or
        // bit-rotted page is caught at backup time rather than discovered to be
        // unrestorable later — the whole point of taking the backup.
        return $"BACKUP DATABASE {ident} TO DISK = N'{disk}' " +
               "WITH FORMAT, INIT, COPY_ONLY, CHECKSUM, NAME = N'MindAttic.Console backup';";
    }

    /// <summary>
    /// sqlcmd args: trusted Windows auth (<c>-E</c>), trust the server cert
    /// (<c>-C</c>, needed since the ODBC driver encrypts by default), and
    /// <c>-b</c> so a backup error returns a non-zero exit code instead of just
    /// printing to stdout.
    /// </summary>
    public static IReadOnlyList<string> BuildArguments(string server, string sql) =>
        ["-S", server, "-E", "-C", "-b", "-Q", sql];

    /// <summary>sqlcmd exits 0 on success; a non-zero code (or a cancellation) is a failure.</summary>
    public static bool ComputeOk(int exitCode, bool cancelled) => !cancelled && exitCode == 0;

    /// <summary>
    /// Backs up each target in order, into <paramref name="targetFolder"/>. Per-db
    /// failures are captured in their result rather than thrown, so one bad
    /// database doesn't abort the rest. <paramref name="onDone"/> fires after each
    /// (wire it to a status label). Cancellation stops before the next database.
    /// </summary>
    public IReadOnlyList<DatabaseBackupResult> Backup(
        IReadOnlyList<BackupTarget> targets,
        string targetFolder,
        Action<DatabaseBackupResult>? onDone = null,
        CancellationToken ct = default)
    {
        var results = new List<DatabaseBackupResult>();
        foreach (var t in targets)
        {
            if (ct.IsCancellationRequested) break;
            var r = BackupOne(t, targetFolder, ct);
            results.Add(r);
            onDone?.Invoke(r);
        }
        return results;
    }

    public DatabaseBackupResult BackupOne(BackupTarget target, string targetFolder, CancellationToken ct = default)
    {
        var file = ResolveBackupFilePath(targetFolder, target.Server, target.Database);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);

        var args = BuildArguments(target.Server, BuildBackupSql(target.Database, file));

        var sw = Stopwatch.StartNew();
        int code;
        string output;
        var cancelled = false;
        try
        {
            (code, output) = run(args, ct);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
            code = -1;
            output = "cancelled";
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 2)
        {
            // ERROR_FILE_NOT_FOUND from Process.Start — sqlcmd isn't on PATH.
            // Say so plainly instead of leaking "The system cannot find the file
            // specified", which reads like a backup-path problem.
            code = -1;
            output = $"'{Executable}' not found on PATH — install the SQL Server command-line tools (sqlcmd).";
        }
        catch (Exception ex)
        {
            // Any other start/IO failure — surface it as a failed result rather
            // than taking down the menu.
            code = -1;
            output = ex.Message;
        }
        sw.Stop();

        var ok = ComputeOk(code, cancelled || ct.IsCancellationRequested);
        if (!ok)
        {
            // A failed/cancelled/killed BACKUP can leave a half-written or
            // zero-byte .bak behind. Removing it keeps a failed run from
            // masquerading as a restorable backup in the dated folder.
            try { if (File.Exists(file)) File.Delete(file); } catch { /* best-effort */ }
        }
        return new DatabaseBackupResult(
            target.Server, target.Database, ok, code, sw.Elapsed, file, ok ? "" : Tail(output, 2000));
    }

    private (int, string) DefaultRun(IReadOnlyList<string> args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(Executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var p = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start {Executable}.");
        var stdoutTask = Task.Run(() => p.StandardOutput.ReadToEnd(), CancellationToken.None);
        var stderrTask = Task.Run(() => p.StandardError.ReadToEnd(), CancellationToken.None);

        while (!p.HasExited)
        {
            if (ct.IsCancellationRequested)
            {
                try { p.Kill(entireProcessTree: true); } catch { }
                break;
            }
            p.WaitForExit(250);
        }

        // Kill() is async; bound the wait before reading ExitCode/draining, same
        // defensive posture as BackupService.Run.
        try { p.WaitForExit(5000); } catch { }
        try { Task.WaitAll([stdoutTask, stderrTask], TimeSpan.FromSeconds(5)); } catch { }

        if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);

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
