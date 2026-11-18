using MindAttic.Vault.Settings.Manifest;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MindAttic.Launcher.Models;

/// <summary>The Launcher's settings (%APPDATA%\MindAttic\MindAttic.Launcher\settings.json), edited in MindAttic.Vault.Desktop.
/// API keys are the shared named keys (Keys tab); the GitHub token is in Vault's Tokens credentials.</summary>
[SettingsApp(Services.SettingsStore.AppBucket, "MindAttic.Launcher", Icon = "rocket",
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

    // ---- Backup (M.LNCH-T128) ----

    /// <summary>
    /// SQL Server instances whose every online user database the backup discovers and backs up, on top of
    /// any database a project lists itself. Null (the key absent) means
    /// <see cref="Services.SqlBackupService.DefaultInstances"/>; an empty list means none.
    /// </summary>
    [Setting("SQL Server instances", Group = "Backup", Order = 1,
        Help = "Every online user database on each instance is backed up to Databases\\<instance>\\<db>.bak. Default: localhost and (localdb)\\MSSQLLocalDB.")]
    public List<string>? BackupSqlInstances { get; set; } = [.. Services.SqlBackupService.DefaultInstances];

    /// <summary>
    /// When true (the default), an instance in <see cref="BackupSqlInstances"/> that no server answers on
    /// (stopped, uninstalled, or sqlcmd itself is gone) is skipped with a log line instead of failing the
    /// backup: after the move to Azure SQL there may be no local SQL Server at all (M.LNCH-T133). An
    /// instance that answers and refuses, and a database whose BACKUP fails, are still failures.
    /// </summary>
    [Setting("Skip unreachable SQL instances", Group = "Backup", Order = 1,
        Help = "On: a listed instance that isn't running or installed is skipped with a log line, not a failed backup. Off: it fails the backup. Empty the instance list to turn the SQL Server step off.")]
    public bool BackupSqlSkipUnreachableInstances { get; set; } = true;

    [Setting("SQL login timeout (seconds)", Group = "Backup", Order = 2,
        Help = "How long sqlcmd waits for an instance to accept the login. A busy server needs more than sqlcmd's own 8 seconds.")]
    public int BackupSqlLoginTimeoutSeconds { get; set; } = Services.SqlBackupService.DefaultLoginTimeoutSeconds;

    [Setting("Upload databases to Azure", Group = "Backup", Order = 3,
        Help = "After the local backup, upload that night's Databases folder to the blob container with the signed-in Azure CLI (az, no keys).")]
    public bool BackupUploadEnabled { get; set; } = true;

    [Setting("Azure storage account", Group = "Backup", Order = 4, Help = "Storage account that holds the off-machine database backups.")]
    public string BackupUploadStorageAccount { get; set; } = Services.AzureBlobBackupService.DefaultStorageAccount;

    [Setting("Azure blob container", Group = "Backup", Order = 5, Help = "Container in that account; each night is a <yyyy-MM-dd>/ prefix in it.")]
    public string BackupUploadContainer { get; set; } = Services.AzureBlobBackupService.DefaultContainer;

    [Setting("Nights kept in Azure", Group = "Backup", Order = 6,
        Help = "After a complete upload, older dated prefixes in the container are deleted so this many nights remain. Separate from the local --keep. 0 or less: never delete.")]
    public int BackupUploadKeepNights { get; set; } = Services.AzureBlobBackupService.DefaultKeepNights;

    [Setting("Upload time limit (hours)", Group = "Backup", Order = 7,
        Help = "An upload still running after this long is stopped and reported as failed, so it can't block the next night's backup. 0: no limit.")]
    public double BackupUploadTimeoutHours { get; set; } = Services.AzureBlobBackupService.DefaultTimeoutHours;

    [Setting("Upload progress interval (minutes)", Group = "Backup", Order = 8, Help = "How often a running upload writes a progress line to the log.")]
    public double BackupUploadProgressMinutes { get; set; } = Services.AzureBlobBackupService.DefaultProgressMinutes;

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
