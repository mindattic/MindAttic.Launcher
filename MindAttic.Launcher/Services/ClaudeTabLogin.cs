using MindAttic.Launcher.Models;

namespace MindAttic.Launcher.Services;

/// <summary>
/// Which login an unscoped Launcher's Claude tab opens on (M.LNCH-T65), asked in the Launcher itself before the tab opens
/// so the tab can go in that person's window, badged and colored as them (M.LNCH-T55), and its host is told the login
/// (<c>--config-dir</c>) rather than asking again inside the tab (M.LNCH-T53). The question is the same list as the
/// tab's: every signed-in login with its usage and whether it already has a CLI, the Orchestrator's Auto pick preselected.
/// A scoped Launcher (M.LNCH-T54), a provider other than Claude and a single signed-in login (no per-CLI login yet) skip it.
/// </summary>
public static class ClaudeTabLogin
{
    /// <param name="Cancelled">The person backed out of the question: open no tab.</param>
    /// <param name="Login">The chosen login's config dir ("" = the global login) to pass as the tab's <c>login</c>; null
    /// when nothing was asked (the tab is built as before).</param>
    public sealed record Choice(bool Cancelled, string? Login)
    {
        public static readonly Choice NotAsked = new(false, null);
    }

    /// <summary>Asks <paramref name="ask"/> which login a <paramref name="provider"/> tab opens on, when there's a choice to
    /// make; its null answer (Esc) cancels the tab.</summary>
    public static Choice Choose(
        AccountScope? scope,
        AgentProvider provider,
        Func<IReadOnlyList<ClaudeAccountOption>, ClaudeAccountOption, ClaudeAccountOption?> ask,
        string? accountsRoot = null,
        DateTime? nowUtc = null)
    {
        if (scope is not null || !WindowsTerminalLauncher.IsClaude(provider)) return Choice.NotAsked;
        if (ClaudeAccountCatalog.PerCliLogins(accountsRoot).Count == 0) return Choice.NotAsked;

        var catalog = ClaudeAccountCatalog.List(nowUtc ?? DateTime.UtcNow, accountsRoot);
        var auto = catalog.FirstOrDefault(o => o.IsAutoPick) ?? catalog[0];
        return ask(catalog, auto) is { } chosen ? new Choice(false, chosen.ConfigDir) : new Choice(true, null);
    }
}
