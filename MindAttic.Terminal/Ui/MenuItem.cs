namespace MindAttic.Terminal.Ui;

/// <summary>
/// Shared shape for selection-prompt entries. Renderer wires
/// <see cref="ToString"/> to "Name  Description" so users see both in the
/// Spectre selection prompt without needing a custom converter every time.
/// </summary>
public sealed class MenuItem
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public object? Tag { get; init; }

    public string Render(int nameWidth)
    {
        var padded = Name.PadRight(nameWidth);
        return string.IsNullOrWhiteSpace(Description)
            ? padded
            : $"{padded}  [grey50]{Description}[/]";
    }
}
