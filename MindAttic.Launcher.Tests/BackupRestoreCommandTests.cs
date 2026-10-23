using MindAttic.Launcher.Commands;
using MindAttic.Launcher.Models;
using MindAttic.Launcher.Services;
using NUnit.Framework;

namespace MindAttic.Launcher.Tests;

[TestFixture]
public sealed class BackupRestoreCommandTests
{
    private string temp = "";

    [SetUp]
    public void SetUp()
    {
        temp = Path.Combine(Path.GetTempPath(), "MindAtticBackupRestoreCmdTests", TestContext.CurrentContext.Test.ID);
        Directory.CreateDirectory(temp);
    }

    [TearDown]
    public void TearDown()
    {
        try { if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    // ---- backup ----

    private static BackupCommand.Deps BackupDeps(
        bool files = true, bool appData = true, bool[]? sql = null, bool[]? sqlite = null,
        IReadOnlyList<string>? folders = null, Action<string>? delete = null, AppSettings? settings = null) =>
        new(
            @"R:\Backup",
            () => @"R:\Backup\2026-01-01",
            t => new BackupResult(files, files ? 0 : 8, TimeSpan.Zero, t, 10),
            t => new AppDataBackupResult(appData, t, 5, TimeSpan.Zero),
            () => settings ?? new AppSettings(),
            s => new DatabaseDiscoveryResult(s, false, [], "no localdb"),
            (_, t) => (sql ?? []).Select((ok, i) => new DatabaseBackupResult("srv", $"db{i}", ok, ok ? 0 : 1, TimeSpan.Zero, "f")).ToList(),
            (_, t) => (sqlite ?? []).Select((ok, i) => new SqliteBackupResult($"p{i}", $"f{i}.db", ok, ok ? 0 : 1, TimeSpan.Zero, "f")).ToList(),
            _ => folders ?? [],
            delete ?? (_ => { }));

    [Test]
    public void Backup_returns_1_when_the_target_folder_cannot_be_resolved()
    {
        var deps = BackupDeps() with { ResolveTargetFolder = () => throw new InvalidOperationException("no free folder") };
        Assert.That(BackupCommand.Run(null, deps), Is.EqualTo(1));
    }

    [Test]
    public void Backup_returns_0_when_everything_succeeds()
    {
        Assert.That(BackupCommand.Run(null, BackupDeps(sql: [true, true], sqlite: [true])), Is.EqualTo(0));
    }

    [Test]
    public void Backup_returns_1_when_files_fail() =>
        Assert.That(BackupCommand.Run(null, BackupDeps(files: false)), Is.EqualTo(1));

    [Test]
    public void Backup_returns_1_when_appdata_fails() =>
        Assert.That(BackupCommand.Run(null, BackupDeps(appData: false)), Is.EqualTo(1));

    [Test]
    public void Backup_returns_1_when_any_sql_database_fails() =>
        Assert.That(BackupCommand.Run(null, BackupDeps(sql: [true, false])), Is.EqualTo(1));

    [Test]
    public void Backup_returns_1_when_any_sqlite_database_fails() =>
        Assert.That(BackupCommand.Run(null, BackupDeps(sqlite: [true, false])), Is.EqualTo(1));

    [Test]
    public void Keep_prunes_only_the_stale_dated_folders()
    {
        string[] folders = [@"R:\Backup\2026-01-03", @"R:\Backup\2026-01-02", @"R:\Backup\2026-01-01", @"R:\Backup\notes"];
        var deleted = new List<string>();
        var code = BackupCommand.Run(2, BackupDeps(folders: folders, delete: deleted.Add));
        Assert.That(code, Is.EqualTo(0));
        Assert.That(deleted, Is.EqualTo(new[] { @"R:\Backup\2026-01-01" }));
    }

    [Test]
    public void Keep_continues_when_a_delete_fails()
    {
        string[] folders = [@"R:\Backup\2026-01-03", @"R:\Backup\2026-01-02", @"R:\Backup\2026-01-01"];
        var attempted = new List<string>();
        var code = BackupCommand.Run(1, BackupDeps(folders: folders, delete: f =>
        {
            attempted.Add(f);
            if (attempted.Count == 1) throw new IOException("locked");
        }));
        Assert.That(code, Is.EqualTo(0));
        Assert.That(attempted, Is.EquivalentTo(new[] { @"R:\Backup\2026-01-02", @"R:\Backup\2026-01-01" }));
    }

    [Test]
    public void Keep_does_not_prune_after_a_failed_backup()
    {
        var deleted = new List<string>();
        var code = BackupCommand.Run(0, BackupDeps(files: false, folders: [@"R:\Backup\2026-01-01"], delete: deleted.Add));
        Assert.That(code, Is.EqualTo(1));
        Assert.That(deleted, Is.Empty);
    }

    [Test]
    public void No_keep_option_prunes_nothing()
    {
        var deleted = new List<string>();
        BackupCommand.Run(null, BackupDeps(folders: [@"R:\Backup\2026-01-01"], delete: deleted.Add));
        Assert.That(deleted, Is.Empty);
    }

    [TestCase(0L, "0 B")]
    [TestCase(1023L, "1023 B")]
    [TestCase(1024L, "1.00 KB")]
    [TestCase(1024L * 1024 - 1, "1024.00 KB")]
    [TestCase(1024L * 1024, "1.00 MB")]
    [TestCase(1024L * 1024 * 1024 - 1, "1024.00 MB")]
    [TestCase(1024L * 1024 * 1024, "1.00 GB")]
    public void FormatBytes_switches_unit_at_each_boundary(long bytes, string expected) =>
        Assert.That(BackupCommand.FormatBytes(bytes).Replace(',', '.'), Is.EqualTo(expected));

    // ---- restore ----

    private sealed class RestoreRecorder
    {
        public readonly List<string> Zips = [];
        public readonly List<(string Server, string Bak)> Baks = [];
        public readonly List<(string Project, string Db, string Dump)> Dumps = [];
    }

    private static RestoreCommand.Deps RestoreDeps(RestoreRecorder r, AppSettings? settings = null, bool ok = true) =>
        new(
            @"C:\AppData",
            zip => { r.Zips.Add(zip); return new AppDataBackupResult(ok, zip, 0, TimeSpan.Zero); },
            (server, bak) => { r.Baks.Add((server, bak)); return new DatabaseRestoreResult(server, Path.GetFileNameWithoutExtension(bak), ok, ok ? 0 : 1, TimeSpan.Zero, bak); },
            () => settings ?? new AppSettings(),
            (project, db, dump) => { r.Dumps.Add((project, db, dump)); return new SqliteRestoreResult(project, db, ok, ok ? 0 : 1, TimeSpan.Zero, dump); });

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase(@"Z:\definitely\not\here")]
    public void Restore_returns_2_for_a_missing_or_blank_from(string? from)
    {
        var r = new RestoreRecorder();
        Assert.That(RestoreCommand.Run(from, null, RestoreDeps(r)), Is.EqualTo(2));
        Assert.That(r.Zips, Is.Empty);
    }

    [Test]
    public void Restore_skips_appdata_when_the_zip_is_absent()
    {
        var r = new RestoreRecorder();
        Assert.That(RestoreCommand.Run(temp, null, RestoreDeps(r)), Is.EqualTo(0));
        Assert.That(r.Zips, Is.Empty);
    }

    [Test]
    public void Restore_restores_appdata_when_the_zip_is_present()
    {
        File.WriteAllText(Path.Combine(temp, AppDataBackupService.ZipFileName), "x");
        var r = new RestoreRecorder();
        Assert.That(RestoreCommand.Run(temp, null, RestoreDeps(r)), Is.EqualTo(0));
        Assert.That(r.Zips, Has.Count.EqualTo(1));
    }

    [Test]
    public void Restore_finds_bak_files_recursively_and_uses_the_default_instance()
    {
        var nested = Path.Combine(temp, SqlBackupService.DatabasesSubfolder, "srv", "deep");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(nested, "One.bak"), "x");
        File.WriteAllText(Path.Combine(temp, SqlBackupService.DatabasesSubfolder, "Two.bak"), "x");
        var r = new RestoreRecorder();
        Assert.That(RestoreCommand.Run(temp, null, RestoreDeps(r)), Is.EqualTo(0));
        Assert.That(r.Baks.Select(b => Path.GetFileName(b.Bak)), Is.EquivalentTo(new[] { "One.bak", "Two.bak" }));
        Assert.That(r.Baks.Select(b => b.Server), Is.All.EqualTo(SqlBackupService.DefaultInstance));
    }

    [Test]
    public void Restore_uses_the_server_option_and_fails_when_a_bak_fails()
    {
        var dbs = Path.Combine(temp, SqlBackupService.DatabasesSubfolder);
        Directory.CreateDirectory(dbs);
        File.WriteAllText(Path.Combine(dbs, "One.bak"), "x");
        var r = new RestoreRecorder();
        Assert.That(RestoreCommand.Run(temp, "other", RestoreDeps(r, ok: false)), Is.EqualTo(1));
        Assert.That(r.Baks.Single().Server, Is.EqualTo("other"));
    }

    [Test]
    public void Restore_matches_sqlite_dumps_to_roster_projects_case_insensitively_and_skips_unmatched()
    {
        var root = Path.Combine(temp, SqlBackupService.DatabasesSubfolder, SqliteBackupService.SqliteSubfolder);
        Directory.CreateDirectory(Path.Combine(root, "MYPROJ"));
        Directory.CreateDirectory(Path.Combine(root, "Ghost"));
        File.WriteAllText(Path.Combine(root, "MYPROJ", "app.sql"), "x");
        File.WriteAllText(Path.Combine(root, "Ghost", "g.sql"), "x");
        var settings = new AppSettings
        {
            Projects = [new Project { Name = "MyProj", Path = @"D:\p\MyProj", SqliteDatabases = [@"D:\live\app.db"] }]
        };
        var r = new RestoreRecorder();
        Assert.That(RestoreCommand.Run(temp, null, RestoreDeps(r, settings)), Is.EqualTo(0));
        var dump = r.Dumps.Single();
        Assert.That(dump.Project, Is.EqualTo("MYPROJ"));
        Assert.That(dump.Db, Is.EqualTo(@"D:\live\app.db"));
        Assert.That(Path.GetFileName(dump.Dump), Is.EqualTo("app.sql"));
    }
}
