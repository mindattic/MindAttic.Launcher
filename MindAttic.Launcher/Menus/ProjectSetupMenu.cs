using MindAttic.Launcher.Services;
using MindAttic.Launcher.Ui;
using MindAttic.Vault.Launch;
using MindAttic.Vault.Settings.Manifest;
using Spectre.Console;

namespace MindAttic.Launcher.Menus;

/// <summary>
/// A project's Settings (alias, description, color scheme, tab color, ...) open MindAttic.Vault.Desktop on the
/// Launcher's tab, scrolled to this project's entry in the roster.
/// </summary>
public sealed class ProjectSetupMenu(SettingsStore store, string projectName)
{
    public void Run()
    {
        if (SettingFor(store, projectName) is not { } setting) return;
        if (VaultSettings.Open(SettingsStore.AppBucket, setting))
        {
            Screen.Notice($"[green]Opened MindAttic Vault at {Markup.Escape(projectName)}'s settings.[/]");
            Thread.Sleep(800);
            return;
        }
        Screen.Notice($"[yellow]MindAttic Vault isn't installed.[/] [grey50]Install it, or edit {Markup.Escape(store.SettingsFilePath)}.[/]");
        Screen.PressAnyKey();
    }

    /// <summary>The full setting name of the project's tab alias row (its index in the roster), or null if it's gone.</summary>
    internal static string? SettingFor(SettingsStore store, string projectName)
    {
        var projects = store.Load().Projects;
        var index = projects.FindIndex(p => string.Equals(p.Name, projectName, StringComparison.OrdinalIgnoreCase));
        return index < 0 ? null : $"{SettingsManifest.PrefixFor(SettingsStore.AppBucket)}:projects:{index}:tabAlias";
    }
}
