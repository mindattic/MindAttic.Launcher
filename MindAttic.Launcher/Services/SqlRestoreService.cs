using System.Diagnostics;

namespace MindAttic.Launcher.Services;

public sealed record SqlDefaultPaths(string Data, string Log);

/// <summary>One row of <c>RESTORE FILELISTONLY</c>: a data ('D') or log ('L') file inside a .bak.</summary>
public sealed record BakFileEntry(string LogicalName, string PhysicalName, string Type);

public sealed record DatabaseRestoreResult(
    string Server, string Database, bool Ok, int ExitCode, TimeSpan Elapsed, string BakFile, string Output = "");

/// <summary>
/// sqlcmd-backed restore of a .bak produced by <see cref="SqlBackupService"/>, deriving the
/// database name from the file name and relocating its data/log files to the target instance's
/// own default paths. Mirrors <c>bob.ps1</c>'s <c>Restore-SingleDatabase</c>/<c>Get-BakFileList</c>/
/// <c>Get-SqlDefaultPaths</c>, now with NUnit coverage instead of only Bob's Pester suite. Same
/// shape as <see cref="SqlBackupService"/>: pure static builders for path/SQL/argument composition
/// (so tests don't touch a real server) plus an injectable runner for the process plumbing.
/// </summary>
public sealed class SqlRestoreService
{
    /// <summary>Runs sqlcmd with the given argument list; returns its exit code and combined output.</summary>
    public delegate (int ExitCode, string Output) SqlcmdRunner(IReadOnlyList<string> args, CancellationToken ct);

    public string Executable { get; }
    private readonly SqlcmdRunner run;

    public SqlRestoreService() : this("sqlcmd") { }

    public SqlRestoreService(string executable, SqlcmdRunner? runner = null)
    {
        Executable = executable;
        run = runner ?? DefaultRun;
    }

    /// <summary>The .bak's file name, minus extension, is the database name — Launcher's own
    /// naming (<see cref="SqlBackupService.ResolveBackupFilePath"/>) is already clean, unlike
    /// Bob's <c>_&lt;timestamp&gt;</c> convention, so no stripping regex is needed.</summary>
    public static string DeriveDatabaseName(string bakFile) => Path.GetFileNameWithoutExtension(bakFile);

    /// <summary>
    /// Raw-row sqlcmd arguments for a query that returns data: trimmed columns (<c>-W</c>),
    /// pipe-separated (<c>-s "|"</c>), no headers (<c>-h -1</c>) — the same convention as
    /// <c>bob.ps1</c>'s <c>Invoke-SqlcmdRaw</c>.
    /// </summary>
    public static IReadOnlyList<string> BuildRawQueryArguments(string server, string sql) =>
        ["-S", server, "-E", "-C", "-W", "-s", "|", "-h", "-1", "-Q", sql];

    public static string BuildFileListSql(string bakFile) =>
        $"RESTORE FILELISTONLY FROM DISK = N'{bakFile.Replace("'", "''")}';";

    public static string BuildDataPathSql() =>
        "SELECT CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS nvarchar(512));";

    public static string BuildLogPathSql() =>
        "SELECT CAST(SERVERPROPERTY('InstanceDefaultLogPath') AS nvarchar(512));";

    public static string BuildExistsSql(string dbName) =>
        $"SELECT name FROM sys.databases WHERE name = N'{dbName.Replace("'", "''")}';";

    public static string BuildSingleUserSql(string dbName) =>
        $"ALTER DATABASE {BracketIdentifier(dbName)} SET SINGLE_USER WITH ROLLBACK IMMEDIATE;";

    /// <summary>sqlcmd args for a fire-and-forget statement: <c>-b</c> so a failure returns a non-zero exit code.</summary>
    public static IReadOnlyList<string> BuildExecArguments(string server, string sql) =>
        ["-S", server, "-E", "-C", "-b", "-Q", sql];

    private static string BracketIdentifier(string name) => "[" + name.Replace("]", "]]") + "]";

    /// <summary>Parses <c>RESTORE FILELISTONLY</c>'s raw pipe-separated rows (see <see cref="BuildRawQueryArguments"/>).</summary>
    public static IReadOnlyList<BakFileEntry> ParseFileList(string output)
    {
        var entries = new List<BakFileEntry>();
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim('\r').Trim();
            if (line.Length == 0 || line.StartsWith('(')) continue; // "(2 rows affected)"
            var cols = line.Split('|', 4);
            if (cols.Length < 3) continue;
            entries.Add(new BakFileEntry(cols[0].Trim(), cols[1].Trim(), cols[2].Trim()));
        }
        return entries;
    }

    /// <summary>First non-blank, non-"(N rows affected)" line of a raw query's output.</summary>
    public static string? FirstDataLine(string output) =>
        output.Split('\n')
            .Select(l => l.Trim('\r').Trim())
            .FirstOrDefault(l => l.Length > 0 && !l.StartsWith('('));

    /// <summary>
    /// Builds the <c>MOVE</c> clauses plus the <c>RESTORE DATABASE ... WITH REPLACE, RECOVERY,
    /// MOVE ...</c> statement. The Nth data file becomes <c>&lt;db&gt;.mdf</c>/<c>&lt;db&gt;N.ndf</c>,
    /// the Nth log file <c>&lt;db&gt;.ldf</c>/<c>&lt;db&gt;N.ldf</c> — same naming scheme as
    /// <c>bob.ps1</c>'s <c>Restore-SingleDatabase</c>.
    /// </summary>
    public static string BuildRestoreSql(string dbName, string bakFile, IReadOnlyList<BakFileEntry> fileList, SqlDefaultPaths paths)
    {
        var dataCount = 0;
        var logCount = 0;
        var moves = new List<string>();
        foreach (var f in fileList)
        {
            string targetFile;
            if (string.Equals(f.Type, "L", StringComparison.OrdinalIgnoreCase))
            {
                logCount++;
                var suffix = logCount > 1 ? logCount.ToString() : "";
                targetFile = $@"{paths.Log}\{dbName}{suffix}.ldf";
            }
            else
            {
                dataCount++;
                var ext = dataCount > 1 ? ".ndf" : ".mdf";
                var suffix = dataCount > 1 ? dataCount.ToString() : "";
                targetFile = $@"{paths.Data}\{dbName}{suffix}{ext}";
            }
            moves.Add($"MOVE N'{f.LogicalName.Replace("'", "''")}' TO N'{targetFile.Replace("'", "''")}'");
        }

        var diskPath = bakFile.Replace("'", "''");
        return $"RESTORE DATABASE {BracketIdentifier(dbName)} FROM DISK = N'{diskPath}' " +
               $"WITH REPLACE, RECOVERY, {string.Join(", ", moves)};";
    }

    public SqlDefaultPaths GetDefaultPaths(string server, CancellationToken ct = default)
    {
        var (dataCode, dataOut) = run(BuildRawQueryArguments(server, BuildDataPathSql()), ct);
        var data = dataCode == 0 ? FirstDataLine(dataOut) : null;
        if (string.IsNullOrWhiteSpace(data))
            throw new InvalidOperationException($"InstanceDefaultDataPath returned empty — is SQL Server running on '{server}'?");
        data = data.TrimEnd('\\');

        var (logCode, logOut) = run(BuildRawQueryArguments(server, BuildLogPathSql()), ct);
        var log = logCode == 0 ? FirstDataLine(logOut) : null;
        log = string.IsNullOrWhiteSpace(log) ? data : log.TrimEnd('\\');

        return new SqlDefaultPaths(data, log);
    }

    public IReadOnlyList<BakFileEntry> GetFileList(string server, string bakFile, CancellationToken ct = default)
    {
        var (code, output) = run(BuildRawQueryArguments(server, BuildFileListSql(bakFile)), ct);
        if (code != 0) throw new InvalidOperationException($"RESTORE FILELISTONLY failed (exit {code}): {Tail(output, 500)}");

        var entries = ParseFileList(output);
        if (entries.Count == 0) throw new InvalidOperationException("RESTORE FILELISTONLY returned no rows");
        return entries;
    }

    /// <summary>
    /// Restores one .bak, deriving the database name from its file name. If a database with that
    /// name already exists (the destination already has this database — the exact case Bob's own
    /// test suite covers), it's dropped to <c>SINGLE_USER</c> and force-replaced rather than left
    /// as a conflict. Every failure is caught and reported in the result; callers decide how to
    /// aggregate across many restores.
    /// </summary>
    public DatabaseRestoreResult RestoreOne(string server, string bakFile, CancellationToken ct = default)
    {
        var dbName = DeriveDatabaseName(bakFile);
        var sw = Stopwatch.StartNew();
        try
        {
            var fileList = GetFileList(server, bakFile, ct);
            var paths = GetDefaultPaths(server, ct);

            var (existsCode, existsOut) = run(BuildRawQueryArguments(server, BuildExistsSql(dbName)), ct);
            if (existsCode == 0 && !string.IsNullOrWhiteSpace(FirstDataLine(existsOut)))
                run(BuildExecArguments(server, BuildSingleUserSql(dbName)), ct);

            var sql = BuildRestoreSql(dbName, bakFile, fileList, paths);
            var (code, output) = run(BuildExecArguments(server, sql), ct);
            sw.Stop();

            var ok = code == 0 && !ct.IsCancellationRequested;
            return new DatabaseRestoreResult(server, dbName, ok, code, sw.Elapsed, bakFile, ok ? "" : Tail(output, 2000));
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new DatabaseRestoreResult(server, dbName, false, -1, sw.Elapsed, bakFile, ex.Message);
        }
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
