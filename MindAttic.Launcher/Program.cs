using System;
using System.IO;
using Microsoft.Data.Sqlite;
using MindAttic.Launcher.Commands;
using MindAttic.Log;
using MindAttic.Log.Extensions;
using MindAttic.Log.Migration;
using MindAttic.Vault.Paths;
using Serilog;
using Spectre.Console.Cli;

// No DI container here — Spectre.Console.Cli's CommandApp constructs commands directly — so this
// wires MindAttic.Log at the one place every run passes through regardless: the entry point
// itself. Launcher has had zero crash visibility of any kind until now (see MindAttic.Log repo's
// docs/MIGRATION.md) — the same gap MediaButler's WPF front door had, fixed the same way: a crash
// log at the boundary, not a rewrite of the many AnsiConsole.MarkupLine calls scattered through
// Commands/Menus/Services/Ui — those are the TUI's actual menu output, not diagnostics to extract.
var logDbPath = VaultPaths.LoggingDbPath("MindAttic.Launcher");

// Phase 2 (MindAttic.Log 3.0.0, see its docs/BIBLE.md §1.1): carry forward any rows left in
// Phase 1's rolled MindAttic.Log.<yyyy-MM>.db files under %LocalAppData%\MindAttic\MindAttic.Launcher\logs
// (LogFileRoller is retired), then remove the now-empty rolled files. Idempotent and a clean
// no-op on every later run.
var oldLogDirectory = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "MindAttic", "MindAttic.Launcher", "logs");
if (Directory.Exists(oldLogDirectory))
{
    foreach (var rolledFile in Directory.GetFiles(oldLogDirectory, "MindAttic.Log.*.db"))
    {
        MindAtticLogMigrator.MigrateFromSqliteAsync(rolledFile, logDbPath, dropSource: true)
            .GetAwaiter().GetResult();
        SqliteConnection.ClearAllPools();
        try { File.Delete(rolledFile); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteToMindAtticLog(new MindAtticLogOptions
    {
        Application = "MindAttic.Launcher",
        DbPath = logDbPath,
    })
    .CreateLogger();

var app = new CommandApp<MainMenuCommand>();

app.Configure(config =>
{
    config.SetApplicationName("MindAttic.Launcher");
    config.AddCommand<HostAgentCommand>("host")
        .WithDescription("Run a coding agent inside this tab (used by 'Open Project Tab').")
        .WithExample("host", "--name", "MindAttic.Vault", "--provider", "Claude");
    config.AddCommand<CommitCommand>("commit")
        .WithDescription("Commit and push one or all MindAttic projects.")
        .WithExample("commit")
        .WithExample("commit", "--project", "MindAttic.Vault", "--message", "Fix readme");
    config.AddCommand<BackupCommand>("backup")
        .WithDescription("Non-interactive backup (files, AppData, SQL Server, SQLite) — what a scheduled task calls.")
        .WithExample("backup")
        .WithExample("backup", "--keep", "30");
    config.AddCommand<RestoreCommand>("restore")
        .WithDescription("Restore AppData, SQL Server and SQLite databases from a dated backup folder.")
        .WithExample("restore", "--from", @"R:\Backup\MindAttic\2026-05-23");
    config.AddCommand<BootstrapCommand>("bootstrap")
        .WithDescription("New-machine setup: install prerequisites, clone every tagged repo, restore the latest reachable backup.")
        .WithExample("bootstrap")
        .WithExample("bootstrap", "--workspace-root", @"D:\Projects\MindAttic");
    config.AddCommand<VersionCommand>("version")
        .WithAlias("--version")
        .WithDescription("Print the MindAttic.Launcher version and exe path.");
});

try
{
    return await app.RunAsync(args);
}
catch (Exception ex)
{
    Log.Fatal(ex, "MindAttic.Launcher crashed");
    throw;
}
finally
{
    Log.CloseAndFlush();
}
