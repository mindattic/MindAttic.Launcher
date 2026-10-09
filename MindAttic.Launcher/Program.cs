using System;
using System.IO;
using MindAttic.Launcher.Commands;
using MindAttic.Log;
using MindAttic.Log.Extensions;
using Serilog;
using Spectre.Console.Cli;

// No DI container here — Spectre.Console.Cli's CommandApp constructs commands directly — so this
// wires MindAttic.Log at the one place every run passes through regardless: the entry point
// itself. Launcher has had zero crash visibility of any kind until now (see MindAttic.Log repo's
// docs/MIGRATION.md) — the same gap MediaButler's WPF front door had, fixed the same way: a crash
// log at the boundary, not a rewrite of the many AnsiConsole.MarkupLine calls scattered through
// Commands/Menus/Services/Ui — those are the TUI's actual menu output, not diagnostics to extract.
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteToMindAtticLog(new MindAtticLogOptions
    {
        Application = "MindAttic.Launcher",
        Destination = LogDestination.Sqlite,
        FileDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MindAttic", "MindAttic.Launcher", "logs"),
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
