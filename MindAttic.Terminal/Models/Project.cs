namespace MindAttic.Terminal.Models;

public sealed class Project
{
    public string Name { get; set; } = "";
    public string? Repo { get; set; }
    public string Path { get; set; } = "";
    public string? Description { get; set; }
    public string? OpenWith { get; set; }
    public string? RunCommand { get; set; }
    public string? TabAlias { get; set; }
    public string? TabColor { get; set; }
    public string? ColorScheme { get; set; }
    public string? Provider { get; set; }
    public bool? MobileEnabled { get; set; }

    public string TabTitle => string.IsNullOrWhiteSpace(TabAlias) ? Name : TabAlias!;
}
