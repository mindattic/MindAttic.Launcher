namespace MindAttic.Terminal.Models;

public sealed class AppSettings
{
    public string? Provider { get; set; }
    public string? WindowsTerminalSettingsPath { get; set; }
    public MobileBridgeSettings Mobile { get; set; } = new();
    public List<AgentProvider> AgentProviders { get; set; } = new();
    public List<Project> Projects { get; set; } = new();
}
