using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
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
        if (string.IsNullOrWhiteSpace(settings.From) || !Directory.Exists(settings.From))
        {
            AnsiConsole.MarkupLine($"[red]--from folder not found: {Markup.Escape(settings.From ?? "")}[/]");
            return 2;
        }

        var from = settings.From;
        var ok = true;

        ok &= RestoreAppData(from);
        ok &= RestoreSqlDatabases(from, settings.Server ?? SqlBackupService.DefaultInstance);
        ok &= RestoreSqliteDatabases(from);

        return ok ? 0 : 1;
    }

    internal static bool RestoreAppData(string from)
    {
        var zipPath = Path.Combine(from, AppDataBackupService.ZipFileName);
        if (!File.Exists(zipPath))
        {
            AnsiConsole.MarkupLine("[white]AppData:[/] [grey50]no AppData.zip found — skipped[/]");
            return true;
        }

        var appData = new AppDataBackupService();
        var r = appData.Restore(zipPath, appData.Source);
        AnsiConsole.MarkupLine(r.Ok
            ? $"[white]AppData:[/] [green]restored[/] -> {Markup.Escape(appData.Source)}"
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
    internal static bool RestoreSqlDatabases(string from, string server)
    {
        var databasesRoot = Path.Combine(from, SqlBackupService.DatabasesSubfolder);
        if (!Directory.Exists(databasesRoot)) return true;

        var baks = Directory.GetFiles(databasesRoot, "*.bak", SearchOption.AllDirectories);
        if (baks.Length == 0) return true;

        var restore = new SqlRestoreService();
        var ok = true;
        foreach (var bak in baks)
        {
            var r = restore.RestoreOne(server, bak);
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
    internal static bool RestoreSqliteDatabases(string from)
    {
        var sqliteRoot = Path.Combine(from, SqlBackupService.DatabasesSubfolder, SqliteBackupService.SqliteSubfolder);
        if (!Directory.Exists(sqliteRoot)) return true;

        var store = new SettingsStore();
        var settings = store.Load();
        var targets = SqliteBackupService.CollectTargets(settings);
        var byProject = targets
            .GroupBy(t => t.Project, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().DatabaseFile, StringComparer.OrdinalIgnoreCase);

        var restore = new SqliteRestoreService();
        var ok = true;
        foreach (var projectDir in Directory.GetDirectories(sqliteRoot))
        {
            var projectName = Path.GetFileName(projectDir);
            foreach (var dump in Directory.GetFiles(projectDir, "*.sql"))
            {
                if (!byProject.TryGetValue(projectName, out var dbFile))
                {
                    AnsiConsole.MarkupLine($"[white]{Markup.Escape(projectName)} ({Markup.Escape(Path.GetFileName(dump))}):[/] [yellow]no matching project in the current roster — skipped[/]");
                    continue;
                }

                var r = restore.RestoreOne(projectName, dbFile, dump);
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
