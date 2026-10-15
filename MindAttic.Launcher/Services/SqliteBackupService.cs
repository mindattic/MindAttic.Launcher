using System.Diagnostics;
using MindAttic.Launcher.Models;

namespace MindAttic.Launcher.Services;

/// <summary>One SQLite database's <c>.dump</c> outcome.</summary>
public sealed record SqliteBackupResult(
    string Project, string DatabaseFile, bool Ok, int ExitCode, TimeSpan Elapsed, string BackupFile, string Output = "");

/// <summary>A (project, database file) pair to back up. Value-equal so duplicates dedupe.</summary>
public readonly record struct SqliteBackupTarget(string Project, string DatabaseFile);

/// <summary>
/// sqlite3-backed full dump (schema + data, as portable SQL text) of each
/// project's SQLite databases into <c>{datedFolder}\Databases\SQLite\{project}\{db}.sql</c>.
/// SQLite has no server to talk to — <c>.dump</c> is its equivalent of SQL
/// Server's <c>BACKUP DATABASE</c>: a single engine-verified snapshot rather
/// than trusting the raw <c>.db</c> file robocopy already copied (which can be
/// mid-write or WAL-split). Mirrors <see cref="SqlBackupService"/>: pure
/// helpers for path/arg/exit-code logic (so tests don't touch a real database)
/// and an injectable runner so the process plumbing can be faked.
/// </summary>
public sealed class SqliteBackupService
{
    public const string DatabasesSubfolder = "Databases";
    public const string SqliteSubfolder = "SQLite";

    /// <summary>Runs sqlite3 with the given argument list; returns its exit code and combined stdout/stderr.</summary>
    public delegate (int ExitCode, string Output) SqliteRunner(IReadOnlyList<string> args, CancellationToken ct);

    public string Executable { get; }
    private readonly SqliteRunner run;

    public SqliteBackupService() : this("sqlite3") { }

    public SqliteBackupService(string executable, SqliteRunner? runner = null)
    {
        Executable = executable;
        run = runner ?? DefaultRun;
    }

    /// <summary>
    /// Flattens the roster into the distinct database files to back up: every
    /// non-blank entry in each project's <see cref="Project.SqliteDatabases"/>,
    /// resolved against that project's <see cref="Project.Path"/> when relative.
    /// The same resolved file named by two projects collapses to one backup.
    /// </summary>
    public static IReadOnlyList<SqliteBackupTarget> CollectTargets(AppSettings settings, IReadOnlySet<string>? taggedFullNames = null)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var targets = new List<SqliteBackupTarget>();
        foreach (var p in ProjectRoster.Sorted(settings, taggedFullNames))
            foreach (var raw in p.SqliteDatabases ?? [])
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                var resolved = ResolveDatabasePath(p.Path, raw);
                if (seen.Add(resolved))
                    targets.Add(new SqliteBackupTarget(p.Name, resolved));
            }
        return targets;
    }

    /// <summary>Absolute paths pass through; a relative one resolves against the project's folder.</summary>
    public static string ResolveDatabasePath(string projectPath, string raw)
    {
        var trimmed = raw.Trim();
        return System.IO.Path.IsPathRooted(trimmed) ? trimmed : System.IO.Path.GetFullPath(System.IO.Path.Combine(projectPath, trimmed));
    }

    /// <summary>
    /// Maps a (project, database file) pair to its .sql path under the dated
    /// folder, scrubbing path-illegal chars from the project name. The project
    /// is part of the path so two projects with a same-named db file (e.g. both
    /// have an <c>app.db</c>) land in distinct files.
    /// </summary>
    public static string ResolveBackupFilePath(string targetFolder, string project, string databaseFile) =>
        System.IO.Path.Combine(targetFolder, DatabasesSubfolder, SqliteSubfolder, SanitizeFileName(project),
            System.IO.Path.GetFileNameWithoutExtension(databaseFile) + ".sql");

    private static string SanitizeFileName(string name)
    {
        var trimmed = name.Trim();
        foreach (var c in System.IO.Path.GetInvalidFileNameChars())
            trimmed = trimmed.Replace(c, '_');
        return string.IsNullOrEmpty(trimmed) ? "project" : trimmed;
    }

    /// <summary>
    /// <c>-readonly</c> guarantees sqlite3 never creates or modifies the source
    /// file — without it, pointing the CLI at a path that doesn't exist (or
    /// isn't actually a database) silently creates an empty one. Combined with
    /// the existence check in <see cref="BackupOne"/>, the source file is never
    /// touched, only read.
    /// </summary>
    public static IReadOnlyList<string> BuildArguments(string databaseFile) => ["-readonly", databaseFile, ".dump"];

    /// <summary>sqlite3 exits 0 on success; a non-zero code (or a cancellation) is a failure.</summary>
    public static bool ComputeOk(int exitCode, bool cancelled) => !cancelled && exitCode == 0;

    /// <summary>
    /// Backs up each target in order, into <paramref name="targetFolder"/>. Per-db
    /// failures are captured in their result rather than thrown, so one bad
    /// database doesn't abort the rest. <paramref name="onDone"/> fires after each
    /// (wire it to a status label). Cancellation stops before the next database.
    /// </summary>
    public IReadOnlyList<SqliteBackupResult> Backup(
        IReadOnlyList<SqliteBackupTarget> targets,
        string targetFolder,
        Action<SqliteBackupResult>? onDone = null,
        CancellationToken ct = default)
    {
        var results = new List<SqliteBackupResult>();
        foreach (var t in targets)
        {
            if (ct.IsCancellationRequested) break;
            var r = BackupOne(t, targetFolder, ct);
            results.Add(r);
            onDone?.Invoke(r);
        }
        return results;
    }

    public SqliteBackupResult BackupOne(SqliteBackupTarget target, string targetFolder, CancellationToken ct = default)
    {
        var file = ResolveBackupFilePath(targetFolder, target.Project, target.DatabaseFile);

        if (!File.Exists(target.DatabaseFile))
        {
            return new SqliteBackupResult(target.Project, target.DatabaseFile, false, -1, TimeSpan.Zero, file,
                $"Database file not found: {target.DatabaseFile}");
        }

        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file)!);
        var args = BuildArguments(target.DatabaseFile);

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
            // ERROR_FILE_NOT_FOUND from Process.Start — sqlite3 isn't on PATH.
            code = -1;
            output = $"'{Executable}' not found on PATH — install the sqlite3 command-line tool.";
        }
        catch (Exception ex)
        {
            code = -1;
            output = ex.Message;
        }
        sw.Stop();

        var ok = ComputeOk(code, cancelled || ct.IsCancellationRequested);
        if (ok)
        {
            try
            {
                File.WriteAllText(file, output);
            }
            catch (Exception ex)
            {
                ok = false;
                code = -1;
                output = ex.Message;
            }
        }
        if (!ok)
        {
            // A failed/cancelled/killed dump can leave a partial .sql behind.
            // Removing it keeps a failed run from masquerading as a restorable
            // backup in the dated folder.
            try { if (File.Exists(file)) File.Delete(file); } catch { /* best-effort */ }
        }

        return new SqliteBackupResult(
            target.Project, target.DatabaseFile, ok, code, sw.Elapsed, file, ok ? "" : Tail(output, 2000));
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

        try { p.WaitForExit(5000); } catch { }
        try { Task.WaitAll([stdoutTask, stderrTask], TimeSpan.FromSeconds(5)); } catch { }

        if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);

        int code;
        try { code = p.ExitCode; }
        catch { code = -1; }

        var stdout = stdoutTask.IsCompletedSuccessfully ? stdoutTask.Result : "";
        var stderr = stderrTask.IsCompletedSuccessfully ? stderrTask.Result : "";
        // On success stdout IS the backup payload — unlike SqlBackupService,
        // keep it separate from stderr rather than joining them, so a stray
        // warning on stderr doesn't get written into the .sql file.
        return (code, code == 0 ? stdout : string.Join("\n", stderr, stdout).Trim());
    }

    private static string Tail(string text, int maxChars)
    {
        if (string.IsNullOrEmpty(text)) return "";
        text = text.Trim();
        return text.Length <= maxChars ? text : text[^maxChars..];
    }
}
