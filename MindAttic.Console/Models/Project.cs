namespace MindAttic.Console.Models;

public sealed class Project
{
    public string Name { get; set; } = "";
    public string? Repo { get; set; }
    public string Path { get; set; } = "";
    public string? Description { get; set; }
    public string? OpenWith { get; set; }
    public string? RunCommand { get; set; }
    public string? TabAlias { get; set; }
    public string? TabColor { get; set; }
    public string? ColorScheme { get; set; }
    public string? Provider { get; set; }

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

    private const string NamePrefix = "MindAttic.";

    /// <summary>
    /// Short label for the wt tab title. An explicit <see cref="TabAlias"/> wins;
    /// otherwise the shared "MindAttic." prefix is stripped so "MindAttic.Legion"
    /// shows as "Legion" — tab titles have very little room and the prefix is
    /// redundant when every project carries it.
    /// </summary>
    public string TabTitle =>
        !string.IsNullOrWhiteSpace(TabAlias) ? TabAlias!
        : Name.StartsWith(NamePrefix, StringComparison.OrdinalIgnoreCase) ? Name[NamePrefix.Length..]
        : Name;
}
