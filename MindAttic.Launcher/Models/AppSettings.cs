using MindAttic.Vault.Settings.Manifest;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MindAttic.Launcher.Models;

/// <summary>The Launcher's settings (%APPDATA%\MindAttic\MindAttic.Launcher\settings.json), edited in MindAttic.Vault.Desktop.
/// API keys are the shared named keys (Keys tab); the GitHub token is in Vault's Tokens credentials.</summary>
[SettingsApp(Services.SettingsStore.AppBucket, "MindAttic Launcher", Icon = "rocket",
    Description = "Project roster, agent CLIs and terminal integration for the MindAttic workspace.")]
[UsesCredential("claude", Purpose = "Claude Code CLI (unless logged into claude.ai)")]
[UsesCredential("gemini", Purpose = "Gemini CLI")]
[UsesCredential("grok", Purpose = "Grok CLI")]
[UsesCredential("kimi", Purpose = "Kimi CLI (synced into its own config)")]
public sealed class AppSettings
{
    [Setting("Windows Terminal settings file", Group = "Terminal", Order = 1,
        Help = "Windows Terminal's settings.json, used to add each project's tab color scheme. Empty: tab colors aren't written.")]
    [PathPicker(PathKind.File, Filter = "settings.json|settings.json|JSON|*.json")]
    public string? WindowsTerminalSettingsPath { get; set; }
    [Setting("Agent CLIs", Group = "Agent CLIs", Order = 1,
        Help = "Each CLI the menus can start. The model is the --model in its run command; remove --model to use the CLI default.")]
    [SettingCollection(ItemName = "agent CLI", ItemLabel = nameof(AgentProvider.Name))]
    public List<AgentProvider> AgentProviders { get; set; } = new();
    [Setting("Projects", Group = "Projects", Order = 1,
        Help = "The roster. Repos tagged 'wip' on GitHub are added and removed automatically on every launch.")]
    [SettingCollection(ItemName = "project", ItemLabel = nameof(Project.Name))]
    public List<Project> Projects { get; set; } = new();

    /// <summary>
    /// Full paths of git repos under the MindAttic root that the user chose to
    /// never auto-add to the roster ("never ask again" during startup discovery).
    /// <see cref="Services.ProjectDiscovery"/> excludes these from its candidate
    /// list so a deliberately-unmanaged repo isn't re-offered every launch.
    /// </summary>
    [Setting("Never offer these repos", Group = "Projects", Order = 2, Help = "Repo folders startup discovery won't offer to add.")]
    public List<string>? DiscoveryIgnore { get; set; }

    /// <summary>
    /// Captures top-level settings keys this app doesn't model (e.g. the "mobile"
    /// block a sibling tool writes) so a Save — triggered by any in-app change —
    /// round-trips them instead of silently wiping them. See
    /// <see cref="Project.Extra"/> for the per-project equivalent.
    /// </summary>
    [JsonExtensionData]
    [SettingIgnore]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}
