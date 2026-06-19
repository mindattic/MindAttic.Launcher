using MindAttic.Launcher.Commands;
using Spectre.Console.Cli;

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

return await app.RunAsync(args);
