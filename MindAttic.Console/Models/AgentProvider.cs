using System.Text.Json;
using System.Text.Json.Serialization;

namespace MindAttic.Console.Models;

public sealed class AgentProvider
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string RunCommand { get; set; } = "";

    /// <summary>
    /// Preserves unknown provider keys across a settings Save, same as
    /// <see cref="AppSettings.Extra"/> — a future schema field shouldn't be wiped
    /// just because this version doesn't model it.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}
