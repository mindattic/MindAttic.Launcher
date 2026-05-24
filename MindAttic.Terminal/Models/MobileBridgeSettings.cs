namespace MindAttic.Terminal.Models;

public sealed class MobileBridgeSettings
{
    public string? ServerUrl { get; set; }

    // Persisted token never reaches disk via this property — it's resolved at
    // runtime from TokenStore (bucket: MindAttic.Mobile, key: Token). The setter
    // exists so the legacy settings.json migration can carry the value into Vault
    // on first run.
    public string? Token { get; set; }

    public bool Enabled { get; set; }
    public bool AllProjects { get; set; }
}
