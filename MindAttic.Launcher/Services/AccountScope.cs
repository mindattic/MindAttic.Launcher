using System.Diagnostics;
using System.Text.Json;

namespace MindAttic.Launcher.Services;

/// <summary>
/// A Launcher scoped to one person (M.LNCH-T54): <c>MindAttic.Launcher --account &lt;email&gt;</c> runs the whole Launcher as
/// that Claude login. Every Claude tab it opens names the login's folder (<see cref="ConfigDir"/>, the per-CLI login under
/// <c>%USERPROFILE%\.claude-accounts\&lt;email&gt;</c>) in its own command, since Windows Terminal tabs don't inherit the
/// Launcher's environment, so every CLI it starts bills that person. Its own window title carries its real initials badge, the
/// same "Initials: Title" every repo CLI's tab carries (<see cref="LoginLook.Badge"/>); its Claude tabs (and its restart) go in
/// that login's own terminal window, badged and colored as it (<see cref="LoginLook"/>, M.LNCH-T55).
/// </summary>
public sealed record AccountScope(string Email, string ConfigDir, string Alias)
{
    /// <summary>The file a login's folder keeps its name in (MindAttic.Orchestrator's <c>LoginLook.FileName</c>, M.ORCH-T614).</summary>
    public const string LookFileName = "mindattic-account.json";

    public static string DefaultAccountsRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude-accounts");

    /// <summary>The email as its login folder is named (trimmed, lower case); null when it isn't one. Mirrors
    /// MindAttic.Orchestrator's <c>AccountDirs.Normalize</c>, so both name the same folder.</summary>
    public static string? Normalize(string? email)
    {
        var e = email?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(e)) return null;
        var at = e.IndexOf('@');
        if (at <= 0 || at != e.LastIndexOf('@') || at == e.Length - 1) return null;
        if (e.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || e.Contains(' ') || e.EndsWith('.')) return null;
        return e;
    }

    /// <summary>The scope for <paramref name="email"/> as its login folder stands now (its alias read afresh); null when
    /// <paramref name="email"/> isn't an email address.</summary>
    public static AccountScope? For(string email, string? accountsRoot = null)
    {
        if (Normalize(email) is not { } e) return null;
        var dir = Path.Combine(accountsRoot ?? DefaultAccountsRoot, e);
        return new AccountScope(e, dir, AliasOf(dir, SignedInEmail(dir) ?? e));
    }

    /// <summary>Whether <see cref="ConfigDir"/> has <see cref="Email"/>'s Claude login signed in.</summary>
    public bool IsSignedIn => SignedInEmail(ConfigDir) is { } signed && string.Equals(signed, Email, StringComparison.OrdinalIgnoreCase);

    /// <summary>The email Claude Code has for the login in <paramref name="folder"/> (<c>oauthAccount.emailAddress</c> in its
    /// <c>.claude.json</c>); null when nobody is signed in there or it can't be read.</summary>
    public static string? SignedInEmail(string folder)
    {
        try
        {
            var path = Path.Combine(folder, ".claude.json");
            if (!File.Exists(path)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            return doc.RootElement.ValueKind == JsonValueKind.Object &&
                   doc.RootElement.TryGetProperty("oauthAccount", out var oauth) && oauth.ValueKind == JsonValueKind.Object &&
                   oauth.TryGetProperty("emailAddress", out var e) && e.ValueKind == JsonValueKind.String &&
                   e.GetString() is { } s && s.Trim().Length > 0
                ? s.Trim()
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// What the login in <paramref name="folder"/>, signed in as <paramref name="email"/>, is called — the Orchestrator's rule
    /// (<c>LoginLook</c>, M.ORCH-T614): the <c>alias</c> in its <see cref="LookFileName"/> while that file is for this email (its
    /// <c>email</c> matches, or it has none and the folder is the per-CLI one named for the email), else the email's part
    /// before the @, else the email itself.
    /// </summary>
    public static string AliasOf(string folder, string email)
    {
        try
        {
            var path = Path.Combine(folder, LookFileName);
            if (File.Exists(path))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                    Belongs(Text(doc.RootElement, "email"), folder, email) &&
                    Text(doc.RootElement, "alias") is { } alias)
                    return alias;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }

        return email.IndexOf('@') is > 0 and var at ? email[..at] : email;
    }

    private static bool Belongs(string? stored, string folder, string email) =>
        stored is not null
            ? string.Equals(stored, email, StringComparison.OrdinalIgnoreCase)
            : string.Equals(Normalize(Path.GetFileName(Path.TrimEndingDirectorySeparator(folder))), email, StringComparison.OrdinalIgnoreCase);

    private static string? Text(JsonElement e, string key) =>
        e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { } s && s.Trim().Length > 0 ? s.Trim() : null;

    /// <summary>Test seam for the sign-in; production runs <c>orchestrator login --account &lt;email&gt;</c> in this console.</summary>
    public static Func<string, int>? SignInOverride { get; set; }

    /// <summary>
    /// Signs <paramref name="email"/>'s own login in when it isn't yet (<c>orchestrator login --account</c>, which prepares the
    /// folder and opens the browser on that account's sign-in), so the scoped Launcher never opens on a login nobody is in.
    /// Returns the scope (re-read, so a freshly set alias shows) or an error to print.
    /// </summary>
    public static (AccountScope? Scope, string? Error) Ensure(string email, string? accountsRoot = null)
    {
        if (For(email, accountsRoot) is not { } scope)
            return (null, $"\"{email}\" isn't an email address: --account takes the account's whole email.");
        if (scope.IsSignedIn) return (scope, null);

        System.Console.WriteLine($"{scope.Email} isn't signed in on this machine yet. Signing it in first (orchestrator login --account {scope.Email})...");
        int code;
        try { code = SignInOverride is not null ? SignInOverride(scope.Email) : RunOrchestratorLogin(scope.Email); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return (null, $"Couldn't run orchestrator login --account {scope.Email} ({ex.Message}). Is MindAttic.Orchestrator installed?");
        }

        var after = For(scope.Email, accountsRoot)!;
        return after.IsSignedIn
            ? (after, null)
            : (null, $"{scope.Email} still isn't signed in (orchestrator login exited {code}); the Launcher won't open on it.");
    }

    private static int RunOrchestratorLogin(string email)
    {
        // Inherits this console: the login prints where to sign in and waits on the browser.
        var psi = new ProcessStartInfo(ExecutableResolver.Resolve("orchestrator")) { UseShellExecute = false };
        psi.ArgumentList.Add("login");
        psi.ArgumentList.Add("--account");
        psi.ArgumentList.Add(email);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("orchestrator didn't start.");
        p.WaitForExit();
        return p.ExitCode;
    }
}
