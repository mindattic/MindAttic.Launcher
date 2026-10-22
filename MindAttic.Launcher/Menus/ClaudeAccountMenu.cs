using MindAttic.Launcher.Models;
using MindAttic.Launcher.Services;
using MindAttic.Launcher.Ui;

namespace MindAttic.Launcher.Menus;

/// <summary>
/// The Launcher's own "which Claude account?" question (M.LNCH-T65), asked before an unscoped Launcher opens a Claude tab
/// while more than one login is signed in: the same list as the tab's own question (<see cref="ClaudeAccountPrompt"/>,
/// M.LNCH-T53), the Auto pick preselected. Esc opens no tab.
/// </summary>
public static class ClaudeAccountMenu
{
    /// <summary>The login a <paramref name="provider"/> tab opens on (see <see cref="ClaudeTabLogin"/>).</summary>
    public static ClaudeTabLogin.Choice Choose(WindowsTerminalLauncher wt, AgentProvider provider, string headerSubtitle) =>
        ClaudeTabLogin.Choose(wt.Scope, provider, (options, auto) => Ask(options, auto, headerSubtitle));

    private static ClaudeAccountOption? Ask(IReadOnlyList<ClaudeAccountOption> options, ClaudeAccountOption auto, string headerSubtitle)
    {
        var autoIndex = Math.Max(0, options.ToList().FindIndex(o => o == auto));
        var items = options
            .Select((o, i) => new MenuItem { Name = o.Describe(), Description = ClaudeAccountPrompt.Details(o, i == autoIndex), Tag = o })
            .ToList();

        Screen.Header("Open Project Tab", headerSubtitle);
        var result = Menu.PromptWithKeys("Open it on which Claude account?", items, customKeys: null, initialIndex: autoIndex);
        return result.Selected?.Tag as ClaudeAccountOption;
    }
}
