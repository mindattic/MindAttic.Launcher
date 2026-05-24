using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Spectre.Console;
using Spectre.Console.Cli;

namespace MindAttic.Terminal.Commands;

[SuppressMessage("Performance", "CA1812", Justification = "Instantiated by Spectre.Console.Cli")]
public sealed class VersionCommand : Command<VersionCommand.Settings>
{
    public sealed class Settings : CommandSettings { }

    public override int Execute(CommandContext context, Settings settings)
    {
        var asm = typeof(VersionCommand).Assembly;
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                   ?? asm.GetName().Version?.ToString()
                   ?? "unknown";
        var location = Environment.ProcessPath ?? "(unknown path)";

        AnsiConsole.MarkupLine($"[cyan1]MindAttic.Terminal[/] [grey50]v[/][yellow]{Markup.Escape(info)}[/]");
        AnsiConsole.MarkupLine($"  [grey50]exe:[/] {Markup.Escape(location)}");
        return 0;
    }
}
