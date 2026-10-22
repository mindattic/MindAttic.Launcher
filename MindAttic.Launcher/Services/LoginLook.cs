using System.Text.Json;

namespace MindAttic.Launcher.Services;

/// <summary>
/// How a Claude login's terminal tabs look (M.LNCH-T55), the same rule as MindAttic.Orchestrator's <c>LoginLook</c>
/// (M.ORCH-T616) so a person's tabs gather in one window whichever app opened them: the Windows Terminal window they go in
/// (<c>wt -w &lt;Window&gt;</c>), the initials badge that starts their titles ("[RD] Repo [Claude]") and their tab color. Read
/// from <see cref="AccountScope.LookFileName"/> in the login's folder (<c>alias</c>, <c>initials</c>, <c>tabColor</c>) while
/// that file is for the login's email; without one, the email's part before the @ names the window and its first two
/// letters are the initials, and the color is picked from the email.
/// </summary>
public sealed record LoginLook(string Window, string Initials, string TabColor)
{
    /// <summary>The login's name as people read it ("Jordan", "Amy Reed"); <see cref="Window"/> is it made safe for <c>wt -w</c>.</summary>
    public string Alias { get; init; } = Window;

    private static readonly string[] Palette = ["#2E7D32", "#1565C0", "#AD1457", "#EF6C00", "#6A1B9A", "#00838F", "#C62828", "#558B2F"];

    /// <summary>The look of the login a tab runs under: <paramref name="configDir"/>'s per-CLI login (its folder is named for
    /// its email), or the global one (null: <c>~/.claude</c>, signed in as <c>~/.claude.json</c>'s email).</summary>
    public static LoginLook ForConfigDir(string? configDir, string? userProfile = null)
    {
        if (string.IsNullOrWhiteSpace(configDir))
        {
            var home = userProfile ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return For(Path.Combine(home, ".claude"), AccountScope.SignedInEmail(home));
        }
        return For(configDir, AccountScope.Normalize(Path.GetFileName(Path.TrimEndingDirectorySeparator(configDir))));
    }

    /// <summary>The look kept in <paramref name="folder"/>, filled in from <paramref name="email"/> where it says nothing.</summary>
    public static LoginLook For(string folder, string? email)
    {
        string? alias = null, initials = null, color = null;
        try
        {
            var path = Path.Combine(folder, AccountScope.LookFileName);
            if (File.Exists(path))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                if (doc.RootElement.ValueKind == JsonValueKind.Object && Belongs(Text(doc.RootElement, "email"), folder, email))
                {
                    alias = Text(doc.RootElement, "alias");
                    initials = Text(doc.RootElement, "initials");
                    color = Text(doc.RootElement, "tabColor");
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }

        var local = DefaultAlias(email);
        var name = alias ?? local ?? "claude";
        var badge = Clean(initials, 3) ?? Clean(Words(alias), 3) ?? Clean(alias, 2) ?? Clean(local, 2) ?? "CL";
        var tab = color is { Length: 7 } c && c[0] == '#' && c[1..].All(Uri.IsHexDigit) ? c.ToUpperInvariant() : Palette[Hash(email ?? folder) % Palette.Length];
        return new LoginLook(WindowName(name), badge, tab) { Alias = name };

        // "Amy Reed" -> "AR"; one word ("Jordan") gives its own first two letters instead.
        static string? Words(string? text) =>
            text?.Split(' ', StringSplitOptions.RemoveEmptyEntries) is { Length: > 1 } words ? string.Concat(words.Select(w => w[0])) : null;

        static string? Clean(string? text, int max) =>
            text is null ? null : new string(text.Where(char.IsLetterOrDigit).Take(max).ToArray()).ToUpperInvariant() is { Length: > 0 } s ? s : null;
    }

    /// <summary><paramref name="title"/> with this login's initials badge in front, as the Orchestrator titles a login's tabs.</summary>
    public string Badge(string title) => $"[{Initials}] {title}";

    /// <summary>What a login is called until it's given a name: its email's part before the @; null without an email.</summary>
    public static string? DefaultAlias(string? email) => email is { } e && e.IndexOf('@') is > 0 and var at ? e[..at] : null;

    /// <summary>Whether a file stamped <paramref name="stored"/> is for <paramref name="email"/>, the login in
    /// <paramref name="folder"/> now: the same email, or no email in the per-CLI login's folder named for it.</summary>
    private static bool Belongs(string? stored, string folder, string? email)
    {
        if (email is null) return false;
        if (stored is not null) return string.Equals(stored, email, StringComparison.OrdinalIgnoreCase);
        return string.Equals(AccountScope.Normalize(Path.GetFileName(Path.TrimEndingDirectorySeparator(folder))), email, StringComparison.OrdinalIgnoreCase);
    }

    private static string? Text(JsonElement e, string key) =>
        e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { } s && s.Trim().Length > 0 ? s.Trim() : null;

    /// <summary>A name <c>wt -w</c> takes as a window's name: a number is a window id, and "new"/"last" mean something else.</summary>
    internal static string WindowName(string name)
    {
        var safe = new string(name.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '-').ToArray()).Trim('-');
        if (safe.Length == 0 || safe.All(char.IsDigit) || safe.Equals("new", StringComparison.OrdinalIgnoreCase) || safe.Equals("last", StringComparison.OrdinalIgnoreCase))
            safe = "claude-" + safe;
        return safe.TrimEnd('-');
    }

    // FNV-1a: string.GetHashCode differs per process, and a login's color must not (and must match the Orchestrator's).
    private static int Hash(string text)
    {
        var h = 2166136261u;
        foreach (var ch in text.ToLowerInvariant()) { h ^= ch; h *= 16777619u; }
        return (int)(h % int.MaxValue);
    }
}
