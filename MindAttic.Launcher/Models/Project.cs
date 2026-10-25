using System.Text.Json;
using System.Text.Json.Serialization;

namespace MindAttic.Launcher.Models;

public sealed class Project
{
    public string Name { get; set; } = "";
    public string? Repo { get; set; }

    /// <summary>
    /// The git remote URL (origin) this repo clones from. Captured when a project
    /// is auto-discovered under the MindAttic root; not consumed by launch logic,
    /// but recorded so the roster carries each repo's canonical clone URL.
    /// </summary>
    public string? RepoUrl { get; set; }

    public string Path { get; set; } = "";
    public string? Description { get; set; }
    public string? OpenWith { get; set; }
    public string? RunCommand { get; set; }
    public string? TabAlias { get; set; }
    public string? TabColor { get; set; }
    public string? ColorScheme { get; set; }

    /// <summary>
    /// SQL Server instance hosting this project's databases (e.g. <c>localhost</c>,
    /// <c>.\SQLEXPRESS</c>). Null/blank falls back to
    /// <see cref="Services.SqlBackupService.DefaultInstance"/>. Only consulted when
    /// <see cref="Databases"/> is non-empty.
    /// </summary>
    public string? SqlServer { get; set; }

    /// <summary>
    /// Names of the SQL Server databases that belong to this project. The backup
    /// step issues a full <c>BACKUP DATABASE</c> (schema + data) for each into the
    /// dated backup folder. Empty means the project has no databases to back up.
    /// </summary>
    public List<string> Databases { get; set; } = new();

    /// <summary>
    /// Paths to this project's SQLite database files — absolute, or relative to
    /// <see cref="Path"/>. The backup step runs <c>sqlite3 &lt;file&gt; .dump</c>
    /// for each into a <c>.sql</c> file in the dated backup folder (schema + data
    /// as portable SQL text, the SQLite equivalent of a SQL Server
    /// <c>BACKUP DATABASE</c> — see <see cref="Services.SqliteBackupService"/>).
    /// The robocopy file snapshot already copies the raw <c>.db</c> file too;
    /// this is the restorable, diffable companion. Empty means no SQLite
    /// databases to back up.
    /// </summary>
    public List<string> SqliteDatabases { get; set; } = new();

    /// <summary>
    /// Captures per-project keys this app doesn't model (e.g. "mobileEnabled")
    /// so they survive a settings Save instead of being silently dropped. See
    /// <see cref="AppSettings.Extra"/>.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    /// <summary>
    /// The project names known right now (M.LNCH-T69): <see cref="Services.SettingsStore"/> replaces this with the
    /// roster it just loaded or saved, every time, so <see cref="ShortName"/> can tell a dot-prefix two or more
    /// projects share (worth stripping, e.g. "GTFO.") from one unique to a single project (kept whole — stripping it
    /// would hide which org it's under) without threading the roster through every call site. Tests set their own.
    /// </summary>
    public static IReadOnlyCollection<string> KnownNames { get; set; } = [];

    /// <summary>
    /// <paramref name="name"/> without its dot-prefix (up to and including the first '.') once two or more of
    /// <see cref="KnownNames"/> share that prefix, case-insensitively (M.LNCH-T69, generalized from a fixed
    /// "GTFO."/"MindAttic."/"Unity." list, M.ORCH-T632): "GTFO.EZ" shows as "EZ" once another "GTFO." project is
    /// known. A name whose prefix nothing else shares, or that is only the prefix, stays whole.
    /// </summary>
    public static string ShortName(string name) =>
        DotPrefix(name) is { } prefix && SharedPrefixes(KnownNames).Contains(prefix) ? name[prefix.Length..] : name;

    private static HashSet<string> SharedPrefixes(IEnumerable<string> names) =>
        new(names.Select(DotPrefix).OfType<string>()
            .GroupBy(p => p, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key),
            StringComparer.OrdinalIgnoreCase);

    private static string? DotPrefix(string name)
    {
        var dot = name.IndexOf('.');
        return dot > 0 && dot < name.Length - 1 ? name[..(dot + 1)] : null;
    }

    /// <summary>
    /// Short label for the wt tab title. An explicit <see cref="TabAlias"/> wins;
    /// otherwise the shared prefix is stripped (<see cref="ShortName"/>) so "MindAttic.Legion"
    /// shows as "Legion" and "GTFO.EZ" as "EZ" — tab titles have very little room and the prefix is
    /// redundant when every project carries it.
    /// </summary>
    [JsonIgnore]
    public string TabTitle =>
        !string.IsNullOrWhiteSpace(TabAlias) ? TabAlias! : ShortName(Name);
}
