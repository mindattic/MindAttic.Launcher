using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using MindAttic.Launcher.Models;
using MindAttic.Launcher.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace MindAttic.Launcher.Commands;

/// <summary>
/// Out-of-menu, non-interactive backup subcommand — what a scheduled task calls instead of the
/// interactive Backup menu. Runs the exact same services
/// (<see cref="BackupService"/>/<see cref="AppDataBackupService"/>/<see cref="SqlBackupService"/>/
/// <see cref="SqliteBackupService"/>) <c>Menus.BackupMenu</c> uses, so "manual" and "automated"
/// backups are provably the same code path, not two implementations that can drift apart.
/// </summary>
[SuppressMessage("Performance", "CA1812", Justification = "Instantiated by Spectre.Console.Cli")]
public sealed class BackupCommand : Command<BackupCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("--keep <N>")]
        [Description("Prune dated backup folders beyond the newest N (default: keep every one).")]
        public int? Keep { get; init; }
    }

    /// <summary>The services <see cref="Run"/> drives, as delegates so tests never reach robocopy/sqlcmd/sqlite3/az.</summary>
    public sealed record Deps(
        string BackupBase,
        Func<string> ResolveTargetFolder,
        Func<string, BackupResult> RunFiles,
        Func<string, AppDataBackupResult> ZipAppData,
        Func<AppSettings> LoadSettings,
        Func<string, AppSettings, DatabaseDiscoveryResult> DiscoverDatabases,
        Func<IReadOnlyList<BackupTarget>, string, AppSettings, IReadOnlyList<DatabaseBackupResult>> BackupSql,
        Func<IReadOnlyList<SqliteBackupTarget>, string, IReadOnlyList<SqliteBackupResult>> BackupSqlite,
        Func<string, IReadOnlyList<string>> ListFolders,
        Action<string> DeleteFolder,
        Func<string, string, AppSettings, AzureUploadResult> UploadDatabases);

    public static Deps Live()
    {
        var store = new SettingsStore();
        var backup = new BackupService();
        var appData = new AppDataBackupService();
        var sqlite = new SqliteBackupService();
        var azure = new AzureBlobBackupService();
        SqlBackupService Sql(AppSettings s) => new()
        {
            LoginTimeoutSeconds = s.BackupSqlLoginTimeoutSeconds,
            OnNote = Note
        };
        return new Deps(
            backup.BackupBase,
            backup.ResolveTargetFolder,
            t => backup.Run(t),
            appData.Zip,
            store.Load,
            (server, s) => Sql(s).DiscoverDatabases(server),
            (targets, t, s) => Sql(s).Backup(targets, t, onDone: ReportDatabase),
            (targets, t) => sqlite.Backup(targets, t),
            baseFolder => Directory.Exists(baseFolder) ? Directory.GetDirectories(baseFolder) : [],
            folder => Directory.Delete(folder, recursive: true),
            (databasesFolder, prefix, s) => azure.UploadAndPrune(databasesFolder, prefix, AzureUploadOptions.From(s), Note));
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        // A run is hours long (file copy, then a multi-GB upload) and starts at midnight with nobody at
        // the keyboard: without this the PC idles into sleep partway through (M.LNCH-T128).
        var previous = SetThreadExecutionState(EsContinuous | EsSystemRequired);
        try
        {
            return Run(settings.Keep, Live());
        }
        finally
        {
            if (previous != 0) SetThreadExecutionState(EsContinuous);
        }
    }

    private const uint EsContinuous = 0x80000000;
    private const uint EsSystemRequired = 0x00000001;

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern uint SetThreadExecutionState(uint esFlags);

    /// <summary>
    /// Order matters (M.LNCH-T128). The workspace file copy takes hours, and a restart, crash or power
    /// loss during it used to end the run before a single database was backed up. So the databases go
    /// first (minutes), then the small steps, then the long file copy, then the local prune, and last
    /// the upload to Azure, which can take hours on a home connection and must never hold up or undo
    /// anything local. Every step is isolated: one throwing or failing never skips the ones after it.
    /// </summary>
    public static int Run(int? keepOption, Deps d)
    {
        string target;
        try
        {
            target = d.ResolveTargetFolder();
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(ex.Message)}[/]");
            Serilog.Log.Error(ex, "MindAttic.Launcher backup could not resolve a target folder");
            return 1;
        }
        AnsiConsole.MarkupLine($"[white]Backup target:[/] {Markup.Escape(target)}");
        // Logged at the start, and again after the databases, so a run that is killed later still leaves a trace.
        Serilog.Log.Information("MindAttic.Launcher backup started: target={Target}", target);

        var ok = true;

        AppSettings settingsData;
        try
        {
            settingsData = d.LoadSettings();
        }
        catch (Exception ex)
        {
            ok = false;
            settingsData = new AppSettings();
            Fail($"Could not read settings ({ex.Message}); using the default backup settings and no per-project databases.");
        }

        // 1. SQL Server: every online user database on every configured instance, plus whatever projects list.
        IReadOnlyList<BackupTarget> dbTargets = [];
        IReadOnlyList<DatabaseBackupResult> dbResults = [];
        try
        {
            dbTargets = SqlBackupService.CollectTargets(settingsData);
            var instances = SqlBackupService.ResolveInstances(settingsData.BackupSqlInstances);
            // M.LNCH-T133: with the databases on Azure SQL there may be no local SQL Server. An empty
            // instance list, or a listed instance nothing answers on, is a skip that says so, not a failure.
            if (instances.Count == 0)
                Note("SQL Server backup: no instances are configured (Backup > SQL Server instances is empty); instance discovery skipped.");
            var unreachable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var instance in instances)
            {
                var found = d.DiscoverDatabases(instance, settingsData);
                if (found.Ok)
                {
                    dbTargets = SqlBackupService.Merge(dbTargets, found.Targets);
                    Note($"Database discovery on {instance}: {found.Targets.Count} found");
                }
                else if (found.Unreachable && settingsData.BackupSqlSkipUnreachableInstances)
                {
                    unreachable.Add(instance);
                    Note($"SQL Server instance {instance} is not reachable (not running or not installed); SKIPPED, nothing backed up from it. {FirstLine(found.Output)}");
                }
                else
                {
                    // An instance that is listed but can't be read means its databases are not being backed up.
                    ok = false;
                    Fail($"Database discovery on {instance} FAILED: {found.Output}");
                }
            }
            if (unreachable.Count > 0)
            {
                // A project-listed database on an instance that isn't there would only fail one BACKUP each.
                var dropped = dbTargets.Count(t => unreachable.Contains(t.Server));
                dbTargets = dbTargets.Where(t => !unreachable.Contains(t.Server)).ToList();
                if (dropped > 0) Note($"SQL Server backup: {dropped} project-listed database(s) on unreachable instance(s) skipped.");
            }
            if (dbTargets.Count == 0)
                Note("SQL Server backup: no local databases to back up; step skipped.");
            dbResults = d.BackupSql(dbTargets, target, settingsData);
        }
        catch (Exception ex)
        {
            ok = false;
            Fail($"SQL Server backup step failed: {ex.Message}");
        }
        ok &= dbResults.All(r => r.Ok);
        Serilog.Log.Information(
            "MindAttic.Launcher backup databases done: target={Target} databases={DbOk}/{DbTotal} failed={Failed}",
            target, dbResults.Count(r => r.Ok), dbResults.Count,
            string.Join(", ", dbResults.Where(r => !r.Ok).Select(r => $"{r.Database}@{r.Server}")));

        // 2. SQLite dumps.
        IReadOnlyList<SqliteBackupResult> sqliteResults = [];
        try
        {
            sqliteResults = d.BackupSqlite(SqliteBackupService.CollectTargets(settingsData), target);
        }
        catch (Exception ex)
        {
            ok = false;
            Fail($"SQLite backup step failed: {ex.Message}");
        }
        foreach (var r in sqliteResults)
        {
            ok &= r.Ok;
            AnsiConsole.MarkupLine(r.Ok
                ? $"[white]{Markup.Escape(r.Project)} ({Markup.Escape(r.DatabaseFile)}):[/] [green]OK[/]"
                : $"[white]{Markup.Escape(r.Project)} ({Markup.Escape(r.DatabaseFile)}):[/] [red]FAILED (exit {r.ExitCode})[/]");
        }

        // 3. AppData settings zip.
        AppDataBackupResult appDataResult;
        try
        {
            appDataResult = d.ZipAppData(target);
        }
        catch (Exception ex)
        {
            appDataResult = new AppDataBackupResult(false, AppDataBackupService.ResolveZipPath(target), 0, TimeSpan.Zero, ex.Message);
        }
        ok &= appDataResult.Ok;
        if (appDataResult.Ok) AnsiConsole.MarkupLine($"[white]AppData:[/] [green]OK[/] ({FormatBytes(appDataResult.Bytes)})");
        else Fail($"AppData zip FAILED: {appDataResult.Output}");

        // 4. The long one: the workspace file copy.
        BackupResult fileResult;
        try
        {
            fileResult = d.RunFiles(target);
        }
        catch (Exception ex)
        {
            fileResult = new BackupResult(false, -1, TimeSpan.Zero, target, Output: ex.Message);
        }
        ok &= fileResult.Ok;
        if (fileResult.Ok) AnsiConsole.MarkupLine($"[white]Files:[/] [green]OK[/] ({FormatBytes(fileResult.BytesCopied)})");
        else Fail($"File copy FAILED (robocopy exit {fileResult.RobocopyExitCode})");

        // 5. Local prune, decided on the local steps alone: the upload below can't block or trigger it.
        var pruned = 0;
        if (keepOption is not null && !ok)
        {
            // A failed run must never delete older backups: they may be the only good ones left.
            AnsiConsole.MarkupLine("[yellow]Backup had failures; skipping prune of older backup folders.[/]");
        }
        else if (keepOption is { } keep)
        {
            var existing = d.ListFolders(d.BackupBase);
            var stale = BackupService.ResolveStaleFolders(existing, keep);
            foreach (var folder in stale)
            {
                try
                {
                    d.DeleteFolder(folder);
                    pruned++;
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[yellow]Could not prune {Markup.Escape(folder)}: {Markup.Escape(ex.Message)}[/]");
                }
            }
            if (pruned > 0) AnsiConsole.MarkupLine($"[white]Pruned:[/] {pruned} old backup folder(s)");
        }

        Serilog.Log.Information(
            "MindAttic.Launcher backup local steps {Outcome}: target={Target} files={FilesOk} appData={AppDataOk} databases={DbOk}/{DbTotal} sqlite={SqliteOk}/{SqliteTotal} pruned={Pruned}",
            ok ? "OK" : "PARTIAL FAILURE", target, fileResult.Ok, appDataResult.Ok,
            dbResults.Count(r => r.Ok), dbResults.Count,
            sqliteResults.Count(r => r.Ok), sqliteResults.Count, pruned);

        // 6. Last: the off-machine copy of the night's databases. Whatever databases did back up are sent
        //    even when another step failed; a failed upload fails the run but deletes nothing.
        AzureUploadResult upload;
        try
        {
            upload = d.UploadDatabases(
                Path.Combine(target, SqlBackupService.DatabasesSubfolder), Path.GetFileName(target), settingsData);
        }
        catch (Exception ex)
        {
            upload = new AzureUploadResult(false, false, $"Azure upload FAILED: {ex.Message}. Nothing was deleted in Azure or on disk.");
        }
        ok &= upload.Ok;
        if (upload.Skipped) Note(upload.Message);
        else if (upload.Ok) Note($"Azure upload OK: {upload.Files} file(s), {FormatBytes(upload.Bytes)}, {upload.PrefixesPruned} old night(s) pruned");
        else Fail(upload.Message);

        Serilog.Log.Information(
            "MindAttic.Launcher backup {Outcome}: target={Target} files={FilesOk} appData={AppDataOk} databases={DbOk}/{DbTotal} sqlite={SqliteOk}/{SqliteTotal} pruned={Pruned} upload={Upload}",
            ok ? "OK" : "PARTIAL FAILURE", target, fileResult.Ok, appDataResult.Ok,
            dbResults.Count(r => r.Ok), dbResults.Count,
            sqliteResults.Count(r => r.Ok), sqliteResults.Count, pruned,
            upload.Skipped ? "off" : upload.Ok ? "OK" : "FAILED");

        return ok ? 0 : 1;
    }

    /// <summary>One database's result, printed and logged as it finishes so a killed run still shows how far it got.</summary>
    private static void ReportDatabase(DatabaseBackupResult r)
    {
        if (r.Ok) Note($"{r.Database}@{r.Server}: OK ({r.Elapsed.TotalSeconds:F0}s)");
        else Fail($"{r.Database}@{r.Server}: FAILED (sqlcmd exit {r.ExitCode}) {r.Output}");
    }

    private static string FirstLine(string text) =>
        (text ?? "").Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "";

    private static void Note(string message)
    {
        AnsiConsole.MarkupLine($"[white]{Markup.Escape(message)}[/]");
        Serilog.Log.Information("MindAttic.Launcher backup: {Message}", message);
    }

    private static void Fail(string message)
    {
        AnsiConsole.MarkupLine($"[red]{Markup.Escape(message)}[/]");
        Serilog.Log.Error("MindAttic.Launcher backup: {Message}", message);
    }

    public static string FormatBytes(long bytes) => BackupCommandFormat.Bytes(bytes);
}
