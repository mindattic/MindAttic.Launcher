namespace MindAttic.Launcher.Ui;

public sealed class MenuItem
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public object? Tag { get; init; }
}
