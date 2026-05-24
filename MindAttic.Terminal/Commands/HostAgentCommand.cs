using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using MindAttic.Terminal.Interop;
using MindAttic.Terminal.Services;
using Spectre.Console.Cli;

namespace MindAttic.Terminal.Commands;

/// <summary>
/// Per-tab agent host. Replaces console-launcher.ps1: looks up the project +
/// provider, splits the provider's RunCommand into argv, sets the tab title
/// and starts the title-pinner, then execs the agent with inherited stdio.
/// </summary>
[SuppressMessage("Performance", "CA1812", Justification = "Instantiated by Spectre.Console.Cli")]
public sealed class HostAgentCommand : Command<HostAgentCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("--name <NAME>")]
        [Description("Project name as listed in settings.json.")]
        public string Name { get; init; } = "";

        [CommandOption("--title <TITLE>")]
        [Description("Tab title (defaults to --name).")]
        public string? Title { get; init; }

        [CommandOption("--provider <PROVIDER>")]
        [Description("Override the project's configured agent provider.")]
        public string? Provider { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Name))
        {
            Console.Error.WriteLine("--name is required.");
            return 64;
        }

        var store = new SettingsStore();
        var registry = new AgentProviderRegistry(store);
        var project = ProjectRoster.FindByName(store.Load(), settings.Name);
        if (project is null)
        {
            Console.Error.WriteLine($"Unknown project: {settings.Name}");
            return 1;
        }

        var provider = !string.IsNullOrWhiteSpace(settings.Provider)
            ? registry.ByKey(settings.Provider) ?? registry.EffectiveProvider(project)
            : registry.EffectiveProvider(project);

        var title = string.IsNullOrWhiteSpace(settings.Title) ? settings.Name : settings.Title!;

        var parts = CommandLineParser.Split(provider.RunCommand);
        if (parts.Length == 0)
        {
            Console.Error.WriteLine($"Provider {provider.Key} has an empty RunCommand.");
            return 2;
        }

        using var pinner = new TitlePinner(title);
        // Per-tab pipe lets the launcher's "Remote Control" menu type
        // /remote-control into every running Claude/Codex tab at once via
        // ConsoleInputInjector. Started before Process.Start so a broadcast
        // landing during agent startup isn't missed.
        using var inputPipe = new HostInputPipeServer(provider.Key);

        var psi = new ProcessStartInfo(parts[0])
        {
            UseShellExecute = false,
            // Working directory matches the wt tab so the agent starts in the
            // right project root; menu code already passes -d to wt, but be
            // defensive in case someone runs `mindattic host` directly.
            WorkingDirectory = Directory.Exists(project.Path) ? project.Path : Environment.CurrentDirectory
        };
        for (var i = 1; i < parts.Length; i++) psi.ArgumentList.Add(parts[i]);

        try
        {
            using var p = Process.Start(psi)
                          ?? throw new InvalidOperationException($"Failed to start {parts[0]}");
            p.WaitForExit();
            return p.ExitCode;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to launch {provider.Key}: {ex.Message}");
            return 3;
        }
    }
}
