namespace MindAttic.Launcher.Services;

/// <summary>
/// Which Claude login a tab's CLI should start on (M.LNCH-T53) — the one engine behind both the Launcher's
/// menus (which always end up here, since opening a project or a general prompt just invokes
/// <c>mindattic host</c> in a new tab) and a bare <c>mindattic host</c> call. See
/// <see cref="MindAttic.Launcher.Commands.HostAgentCommand"/> for the only caller.
/// </summary>
public static class ClaudeAccountResolver
{
    /// <param name="ConfigDir">What to pass to <see cref="ClaudeLogin.Apply"/> ("" for the global login).</param>
    /// <param name="ShowInTitle">Whether the tab title should be account-stamped: true whenever a choice
    /// existed (an explicit --account, or more than one signed-in login), so a tab is never left ambiguous
    /// about which login it's running under once there's more than one to confuse it with.</param>
    /// <param name="Error">Set only for a caller mistake (an unknown <c>--account</c> email) — the caller
    /// should print this and exit non-zero rather than silently falling back to the global login.</param>
    public sealed record Result(string ConfigDir, bool ShowInTitle, string? Error);

    /// <summary>
    /// Resolves the login. <paramref name="requestedEmail"/> (<c>--account</c>) wins outright and fails loud
    /// when it doesn't name a signed-in per-CLI login (the same class of caller mistake <c>--provider</c>/
    /// <c>--name</c> already treat as fail-loud). Otherwise, a single signed-in login (just the global one, no
    /// <c>~/.claude-accounts</c> folder yet) is used with no question asked. Otherwise, when
    /// <paramref name="interactive"/>, <paramref name="choose"/> is offered the full catalog with the
    /// Orchestrator's Auto pick preselected; its answer — or the Auto pick itself, when it declines, there's
    /// no <paramref name="choose"/>, or this isn't interactive — decides.
    /// </summary>
    public static Result Resolve(
        string? requestedEmail,
        bool interactive,
        Func<IReadOnlyList<ClaudeAccountOption>, ClaudeAccountOption, ClaudeAccountOption?>? choose = null,
        string? accountsRoot = null,
        DateTime? nowUtc = null)
    {
        if (!string.IsNullOrWhiteSpace(requestedEmail))
        {
            var dir = ClaudeAccountCatalog.ConfigDirFor(requestedEmail, accountsRoot);
            return Directory.Exists(dir)
                ? new Result(dir, true, null)
                : new Result("", false, $"Unknown account: {requestedEmail}");
        }

        if (ClaudeAccountCatalog.PerCliLogins(accountsRoot).Count == 0)
            return new Result("", false, null);

        var catalog = ClaudeAccountCatalog.List(nowUtc ?? DateTime.UtcNow, accountsRoot);
        var auto = catalog.FirstOrDefault(o => o.IsAutoPick) ?? catalog[0];
        var chosen = interactive ? choose?.Invoke(catalog, auto) ?? auto : auto;
        return new Result(chosen.ConfigDir, true, null);
    }
}
