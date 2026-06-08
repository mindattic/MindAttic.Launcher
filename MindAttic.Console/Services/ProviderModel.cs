using System.Text.RegularExpressions;

namespace MindAttic.Console.Services;

/// <summary>
/// Reads and rewrites the model selection embedded in a provider's
/// <c>RunCommand</c> — the <c>--model &lt;id&gt;</c> (or <c>-m &lt;id&gt;</c>) flag.
/// The RunCommand stays the single source of truth that the launcher splits and
/// runs (<see cref="MindAttic.Console.Interop.CommandLineParser"/>); the Settings
/// screen edits just the model token so the user never has to retype the whole
/// command line.
/// </summary>
public static partial class ProviderModel
{
    // Leading whitespace is part of the match so clearing the flag doesn't leave
    // a double space behind. `--model` is listed before `-m` so the longer flag
    // wins; the value is a quoted string or a single whitespace-free token.
    [GeneratedRegex("""(?<lead>\s*)(?<flag>--model|-m)[=\s]+(?<value>"[^"]*"|\S+)""",
        RegexOptions.IgnoreCase)]
    private static partial Regex ModelFlag();

    /// <summary>The model id in <paramref name="runCommand"/>, or null if none is set.</summary>
    public static string? Get(string? runCommand)
    {
        if (string.IsNullOrWhiteSpace(runCommand)) return null;
        var m = ModelFlag().Match(runCommand);
        return m.Success ? Unquote(m.Groups["value"].Value) : null;
    }

    /// <summary>
    /// Returns <paramref name="runCommand"/> with its model set to
    /// <paramref name="model"/>. A blank model removes the flag entirely so the
    /// CLI falls back to its own default. An existing flag is rewritten in place
    /// (preserving the rest of the command); otherwise <c>--model &lt;id&gt;</c> is
    /// appended.
    /// </summary>
    public static string Set(string? runCommand, string? model)
    {
        var command = runCommand ?? "";
        var trimmed = model?.Trim();

        if (string.IsNullOrEmpty(trimmed))
            return ModelFlag().Replace(command, "").Trim();

        if (ModelFlag().IsMatch(command))
            return ModelFlag().Replace(command, m => $"{m.Groups["lead"].Value}--model {trimmed}");

        return string.IsNullOrWhiteSpace(command) ? $"--model {trimmed}" : $"{command.TrimEnd()} --model {trimmed}";
    }

    private static string Unquote(string value) =>
        value.Length >= 2 && value[0] == '"' && value[^1] == '"' ? value[1..^1] : value;
}
