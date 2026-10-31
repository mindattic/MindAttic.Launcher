using MindAttic.Launcher.Services;
using MindAttic.Launcher.Ui;
using MindAttic.Vault.Launch;
using Spectre.Console;

namespace MindAttic.Launcher.Menus;

/// <summary>
/// "Open settings": the Launcher's settings (agent CLIs and their models, the roster, the Windows Terminal path),
/// its API keys (Keys tab) and the GitHub token are all edited in MindAttic.Vault.Desktop.
/// </summary>
public sealed class SettingsMenu(AgentProviderRegistry providers, SettingsStore store)
{
    public void Run()
    {
        if (Open(VaultSettings.Open))
        {
            Screen.Notice("[green]Opened MindAttic Vault at the Launcher's settings.[/] [grey50]Changes apply the next time a menu loads them.[/]");
            Thread.Sleep(800);
            return;
        }
        Screen.Notice($"[yellow]MindAttic Vault isn't installed.[/] [grey50]Install it, or edit {Markup.Escape(store.SettingsFilePath)}.[/]");
        Screen.PressAnyKey();
    }

    /// <summary>Opens the Launcher's tab through <paramref name="open"/>; the seam the tests use.</summary>
    internal static bool Open(Func<string, string?, bool> open) => open(SettingsStore.AppBucket, null);

    internal static string Mask(string token) =>
        token.Length <= 8 ? "••••" : $"{token[..4]}…{token[^4..]}";

    internal AgentProviderRegistry Providers => providers;
}
