namespace MindAttic.Launcher.Ui;

public sealed class MenuItem
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public object? Tag { get; init; }

    /// <summary>Draw a blank line above this row, setting it apart (e.g. "&lt; Back", "Quit").</summary>
    public bool SpacerBefore { get; init; }
}
