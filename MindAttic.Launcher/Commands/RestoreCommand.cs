using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using MindAttic.Launcher.Models;
using MindAttic.Launcher.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace MindAttic.Launcher.Commands;

/// <summary>
/// Out-of-menu restore subcommand. Reads a dated folder produced by <c>backup</c> (or the
/// interactive Backup menu) and restores AppData, every SQL Server .bak and every SQLite dump it
/// finds. The counterpart Bob's <c>--unpack</c> doesn't have in Launcher today — recovery onto an
/// already-cloned, already-running workspace, not a from-scratch bootstrap (see <c>bootstrap</c>
/// for that).
/// </summary>
[SuppressMessage("Performance", "CA1812", Justification = "Instantiated by Spectre.Console.Cli")]
public sealed class RestoreCommand : Command<RestoreCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("--from <FOLDER>")]
        [Description("Dated backup folder to restore from (as produced by 'backup' or the Backup menu).")]
        public string? From { get; init; }

        [CommandOption("--server <SERVER>")]
        [Description("SQL Server instance to restore every .bak into (defaults to localhost).")]
        public string? Server { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        return Run(settings.From, settings.Server, Deps.Live());
    }

    /// <summary>The restore services as delegates so tests never reach sqlcmd/sqlite3 or the real AppData.</summary>
    public sealed record Deps(
        string AppDataTarget,
        Func<string, AppDataBackupResult> RestoreAppDataZip,
        Func<string, string, DatabaseRestoreResult> RestoreBak,
        Func<AppSettings> LoadSettings,
        Func<string, string, string, SqliteRestoreResult> RestoreDump)
    {
        public static Deps Live()
        {
            var appData = new AppDataBackupService();
            var sql = new SqlRestoreService();
            var sqlite = new SqliteRestoreService();
            return new Deps(
                appData.Source,
                zip => appData.Restore(zip, appData.Source),
                (server, bak) => sql.RestoreOne(server, bak),
                () => new SettingsStore().Load(),
                (project, dbFile, dump) => sqlite.RestoreOne(project, dbFile, dump));
        }
    }

    /// <summary>The exit code for a restore: 2 for a missing/blank --from, else 0 only if every part succeeded.</summary>
    public static int Run(string? from, string? server, Deps d)
    {
        if (string.IsNullOrWhiteSpace(from) || !Directory.Exists(from))
        {
            AnsiConsole.MarkupLine($"[red]--from folder not found: {Markup.Escape(from ?? "")}[/]");
            return 2;
        }
        var ok = RestoreAppData(from, d);
        ok &= RestoreSqlDatabases(from, server ?? SqlBackupService.DefaultInstance, d);
        ok &= RestoreSqliteDatabases(from, d);
        return ok ? 0 : 1;
    }

    internal static bool RestoreAppData(string from) => RestoreAppData(from, Deps.Live());

    public static bool RestoreAppData(string from, Deps d)
    {
        var zipPath = Path.Combine(from, AppDataBackupService.ZipFileName);
        if (!File.Exists(zipPath))
        {
            AnsiConsole.MarkupLine("[white]AppData:[/] [grey50]no AppData.zip found — skipped[/]");
            return true;
        }

        var r = d.RestoreAppDataZip(zipPath);
        AnsiConsole.MarkupLine(r.Ok
            ? $"[white]AppData:[/] [green]restored[/] -> {Markup.Escape(d.AppDataTarget)}"
            : $"[white]AppData:[/] [red]FAILED[/] {Markup.Escape(r.Output)}");
        return r.Ok;
    }

    /// <summary>
    /// Restores every .bak found anywhere under <c>{from}\Databases\</c> (excluding the SQLite
    /// subfolder) into <paramref name="server"/>. One server for the whole run, same as Bob's own
    /// single <c>-SqlServer</c> override — the per-server subfolder name
    /// (<see cref="SqlBackupService.ResolveBackupFilePath"/>) is sanitized for the filesystem and
    /// not always reversible (e.g. <c>(localdb)\MSSQLLocalDB</c>), so it's not used to resolve a
    /// per-file server; pass <c>--server</c> for anything other than the default instance.
    /// </summary>
    internal static bool RestoreSqlDatabases(string from, string server) => RestoreSqlDatabases(from, server, Deps.Live());

    public static bool RestoreSqlDatabases(string from, string server, Deps d)
    {
        var databasesRoot = Path.Combine(from, SqlBackupService.DatabasesSubfolder);
        if (!Directory.Exists(databasesRoot)) return true;

        var baks = Directory.GetFiles(databasesRoot, "*.bak", SearchOption.AllDirectories);
        if (baks.Length == 0) return true;

        var ok = true;
        foreach (var bak in baks)
        {
            var r = d.RestoreBak(server, bak);
            ok &= r.Ok;
            AnsiConsole.MarkupLine(r.Ok
                ? $"[white]{Markup.Escape(r.Database)}@{Markup.Escape(r.Server)}:[/] [green]restored[/] ({Format(r.Elapsed)})"
                : $"[white]{Markup.Escape(r.Database)}@{Markup.Escape(r.Server)}:[/] [red]FAILED (exit {r.ExitCode})[/] {Markup.Escape(r.Output)}");
        }
        return ok;
    }

    /// <summary>
    /// Matches each <c>{from}\Databases\SQLite\{project}\{db}.sql</c> dump against the current
    /// roster's configured SQLite file paths by project name, so the restore lands back at the
    /// live path the project actually reads from rather than the sanitized backup-folder path.
    /// A dump with no matching project in the current roster is reported and skipped — there's
    /// nowhere known to put it back.
    /// </summary>
    internal static bool RestoreSqliteDatabases(string from) => RestoreSqliteDatabases(from, Deps.Live());

    public static bool RestoreSqliteDatabases(string from, Deps d)
    {
        var sqliteRoot = Path.Combine(from, SqlBackupService.DatabasesSubfolder, SqliteBackupService.SqliteSubfolder);
        if (!Directory.Exists(sqliteRoot)) return true;

        var settings = d.LoadSettings();
        var targets = SqliteBackupService.CollectTargets(settings);
        // A project can list several SQLite files; each dump is named after its file's stem, so match on
        // (project, stem) — keying on the project alone restored every dump over the project's first file.
        var byProject = targets
            .GroupBy(t => t.Project, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var ok = true;
        foreach (var projectDir in Directory.GetDirectories(sqliteRoot))
        {
            var projectName = Path.GetFileName(projectDir);
            foreach (var dump in Directory.GetFiles(projectDir, "*.sql"))
            {
                string? dbFile = null;
                if (byProject.TryGetValue(projectName, out var projectDbs))
                {
                    var stem = Path.GetFileNameWithoutExtension(dump);
                    dbFile = projectDbs.FirstOrDefault(t => string.Equals(Path.GetFileNameWithoutExtension(t.DatabaseFile), stem, StringComparison.OrdinalIgnoreCase)).DatabaseFile
                        ?? (projectDbs.Count == 1 ? projectDbs[0].DatabaseFile : null);
                }
                if (dbFile is null)
                {
                    AnsiConsole.MarkupLine($"[white]{Markup.Escape(projectName)} ({Markup.Escape(Path.GetFileName(dump))}):[/] [yellow]no matching project in the current roster — skipped[/]");
                    continue;
                }

                var r = d.RestoreDump(projectName, dbFile, dump);
                ok &= r.Ok;
                AnsiConsole.MarkupLine(r.Ok
                    ? $"[white]{Markup.Escape(r.Project)}:[/] [green]restored[/] -> {Markup.Escape(r.DatabaseFile)} ({Format(r.Elapsed)})"
                    : $"[white]{Markup.Escape(r.Project)}:[/] [red]FAILED (exit {r.ExitCode})[/] {Markup.Escape(r.Output)}");
            }
        }
        return ok;
    }

    private static string Format(TimeSpan t) =>
        t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
}
