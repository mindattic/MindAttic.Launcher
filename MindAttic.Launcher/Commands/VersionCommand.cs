using System.Diagnostics.CodeAnalysis;
using MindAttic.Launcher.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace MindAttic.Launcher.Commands;

[SuppressMessage("Performance", "CA1812", Justification = "Instantiated by Spectre.Console.Cli")]
public sealed class VersionCommand : Command<VersionCommand.Settings>
{
    public sealed class Settings : CommandSettings { }

    public override int Execute(CommandContext context, Settings settings)
    {
        var location = Environment.ProcessPath ?? "(unknown path)";

        AnsiConsole.MarkupLine($"[cyan1]MindAttic.Launcher[/] [yellow]{Markup.Escape(AppVersion.Tag)}[/]");
        AnsiConsole.MarkupLine($"  [grey50]exe:[/] {Markup.Escape(location)}");
        return 0;
    }
}
