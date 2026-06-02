using System.Text.Json;
using System.Text.Json.Serialization;

namespace MindAttic.Console.Models;

public sealed class AppSettings
{
    public string? Provider { get; set; }
    public string? WindowsTerminalSettingsPath { get; set; }
    public List<AgentProvider> AgentProviders { get; set; } = new();
    public List<Project> Projects { get; set; } = new();

    /// <summary>
    /// Captures top-level settings keys this app doesn't model (e.g. the "mobile"
    /// block a sibling tool writes) so a Save — triggered by any in-app change
    /// like setting a provider — round-trips them instead of silently wiping
    /// them. See <see cref="Project.Extra"/> for the per-project equivalent.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}
