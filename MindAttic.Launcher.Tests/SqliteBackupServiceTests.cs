using MindAttic.Launcher.Models;
using MindAttic.Launcher.Services;
using NUnit.Framework;

namespace MindAttic.Launcher.Tests;

[TestFixture]
public sealed class SqliteBackupServiceTests
{
    private string tempFolder = "";

    [SetUp]
    public void SetUp()
    {
        tempFolder = Path.Combine(Path.GetTempPath(), "MindAtticSqliteBackupTests", TestContext.CurrentContext.Test.ID);
        Directory.CreateDirectory(tempFolder);
    }

    [TearDown]
    public void TearDown()
    {
        try { if (Directory.Exists(tempFolder)) Directory.Delete(tempFolder, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    [Test]
    public void ResolveDatabasePath_passes_through_an_absolute_path()
    {
        Assert.That(SqliteBackupService.ResolveDatabasePath(@"D:\Projects\MindAttic\Foo", @"R:\elsewhere\app.db"),
            Is.EqualTo(@"R:\elsewhere\app.db"));
    }

    [Test]
    public void ResolveDatabasePath_resolves_a_relative_path_against_the_project_folder()
    {
        Assert.That(SqliteBackupService.ResolveDatabasePath(@"D:\Projects\MindAttic\Foo", @"data\app.db"),
            Is.EqualTo(@"D:\Projects\MindAttic\Foo\data\app.db"));
    }

    [Test]
    public void ResolveBackupFilePath_places_sql_under_databases_sqlite_subfolder()
    {
        Assert.That(
            SqliteBackupService.ResolveBackupFilePath(@"R:\Backup\MindAttic\2026-05-23", "Legion", @"D:\Legion\app.db"),
            Is.EqualTo(@"R:\Backup\MindAttic\2026-05-23\Databases\SQLite\Legion\app.sql"));
    }

    [Test]
    public void ResolveBackupFilePath_namespaces_by_project_so_same_named_db_does_not_collide()
    {
        var a = SqliteBackupService.ResolveBackupFilePath(@"R:\Backup", "Alpha", @"D:\Alpha\app.db");
        var b = SqliteBackupService.ResolveBackupFilePath(@"R:\Backup", "Beta", @"D:\Beta\app.db");
        Assert.That(a, Is.Not.EqualTo(b));
    }

    [Test]
    public void ResolveBackupFilePath_scrubs_path_illegal_characters_in_project_name()
    {
        var path = SqliteBackupService.ResolveBackupFilePath(@"R:\Backup", "weird:name*proj", @"D:\x\app.db");
        Assert.That(Path.GetFileName(Path.GetDirectoryName(path)), Is.EqualTo("weird_name_proj"));
    }

    [Test]
    public void BuildArguments_opens_readonly_and_dumps()
    {
        var args = SqliteBackupService.BuildArguments(@"D:\Legion\app.db");
        Assert.That(args, Is.EqualTo(new[] { "-readonly", @"D:\Legion\app.db", ".dump" }));
    }

    [TestCase(0, false, ExpectedResult = true)]
    [TestCase(1, false, ExpectedResult = false)]
    [TestCase(-1, false, ExpectedResult = false)]
    [TestCase(0, true, ExpectedResult = false)]
    public bool ComputeOk_only_zero_uncancelled_is_success(int exitCode, bool cancelled) =>
        SqliteBackupService.ComputeOk(exitCode, cancelled);

    [Test]
    public void CollectTargets_flattens_projects_resolves_paths_skips_blanks_and_dedupes()
    {
        var settings = new AppSettings
        {
            Projects =
            [
                new Project { Name = "Alpha", Path = @"D:\Alpha", SqliteDatabases = [@"data\app.db", "  ", @"R:\shared.db"] },
                new Project { Name = "Beta", Path = @"D:\Beta", SqliteDatabases = [@"data\app.db"] }, // different project -> distinct resolved path
                new Project { Name = "Gamma", Path = @"D:\Gamma", SqliteDatabases = [@"R:\shared.db"] }, // dup of Alpha's absolute path
                new Project { Name = "Delta", Path = @"D:\Delta" } // no sqlite databases
            ]
        };

        var targets = SqliteBackupService.CollectTargets(settings);

        Assert.That(targets, Is.EquivalentTo(new[]
        {
            new SqliteBackupTarget("Alpha", @"D:\Alpha\data\app.db"),
            new SqliteBackupTarget("Alpha", @"R:\shared.db"),
            new SqliteBackupTarget("Beta", @"D:\Beta\data\app.db")
        }));
    }

    [Test]
    public void BackupOne_fails_without_invoking_sqlite3_when_the_database_file_is_missing()
    {
        var invoked = false;
        var subject = new SqliteBackupService("sqlite3", (_, _) => { invoked = true; return (0, "dump"); });
        var missingDb = Path.Combine(tempFolder, "nope.db");

        var result = subject.BackupOne(new SqliteBackupTarget("Legion", missingDb), tempFolder);

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.False);
            Assert.That(result.Output, Does.Contain("not found"));
            Assert.That(invoked, Is.False, "sqlite3 must never be invoked against a nonexistent path");
        });
    }

    [Test]
    public void BackupOne_writes_the_dump_output_to_the_sql_file_on_success()
    {
        var dbFile = Path.Combine(tempFolder, "app.db");
        File.WriteAllText(dbFile, "fake sqlite bytes");
        var subject = new SqliteBackupService("sqlite3", (_, _) => (0, "PRAGMA foreign_keys=OFF;\nCOMMIT;\n"));

        var result = subject.BackupOne(new SqliteBackupTarget("Legion", dbFile), tempFolder);

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.True);
            Assert.That(result.BackupFile, Is.EqualTo(Path.Combine(tempFolder, "Databases", "SQLite", "Legion", "app.sql")));
            Assert.That(File.Exists(result.BackupFile), Is.True);
            Assert.That(File.ReadAllText(result.BackupFile), Does.Contain("COMMIT;"));
        });
    }

    [Test]
    public void BackupOne_captures_failure_output_without_throwing_and_leaves_no_partial_file()
    {
        var dbFile = Path.Combine(tempFolder, "app.db");
        File.WriteAllText(dbFile, "fake sqlite bytes");
        var subject = new SqliteBackupService("sqlite3", (_, _) => (1, "Error: database disk image is malformed"));

        var result = subject.BackupOne(new SqliteBackupTarget("Legion", dbFile), tempFolder);

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.False);
            Assert.That(result.ExitCode, Is.EqualTo(1));
            Assert.That(result.Output, Does.Contain("malformed"));
            Assert.That(File.Exists(result.BackupFile), Is.False);
        });
    }

    [Test]
    public void BackupOne_gives_a_clear_message_when_sqlite3_is_not_on_path()
    {
        var dbFile = Path.Combine(tempFolder, "app.db");
        File.WriteAllText(dbFile, "fake sqlite bytes");
        var subject = new SqliteBackupService("sqlite3",
            (_, _) => throw new System.ComponentModel.Win32Exception(2, "The system cannot find the file specified."));

        var result = subject.BackupOne(new SqliteBackupTarget("Legion", dbFile), tempFolder);

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.False);
            Assert.That(result.Output, Does.Contain("not found on PATH"));
            Assert.That(result.Output, Does.Contain("sqlite3"));
        });
    }

    [Test]
    public void Backup_runs_every_target_and_fires_onDone_for_each()
    {
        var dbA = Path.Combine(tempFolder, "a.db");
        var dbB = Path.Combine(tempFolder, "b.db");
        File.WriteAllText(dbA, "x");
        File.WriteAllText(dbB, "x");
        var subject = new SqliteBackupService("sqlite3", (_, _) => (0, "dump"));
        var targets = new[]
        {
            new SqliteBackupTarget("One", dbA),
            new SqliteBackupTarget("Two", dbB)
        };
        var done = new List<string>();

        var results = subject.Backup(targets, tempFolder, onDone: r => done.Add(r.Project));

        Assert.Multiple(() =>
        {
            Assert.That(results.Select(r => r.Project), Is.EqualTo(new[] { "One", "Two" }));
            Assert.That(results.All(r => r.Ok), Is.True);
            Assert.That(done, Is.EqualTo(new[] { "One", "Two" }));
        });
    }

    [Test]
    public void Backup_stops_before_remaining_targets_when_cancelled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var subject = new SqliteBackupService("sqlite3", (_, _) => (0, "dump"));
        var targets = new[]
        {
            new SqliteBackupTarget("One", Path.Combine(tempFolder, "a.db")),
            new SqliteBackupTarget("Two", Path.Combine(tempFolder, "b.db"))
        };

        var results = subject.Backup(targets, tempFolder, ct: cts.Token);

        Assert.That(results, Is.Empty);
    }

    [Test]
    public void Backup_numbers_a_dump_whose_file_stem_collides_with_an_earlier_one()
    {
        var a = Path.Combine(tempFolder, "a", "app.db");
        var b = Path.Combine(tempFolder, "b", "app.db");
        Directory.CreateDirectory(Path.GetDirectoryName(a)!);
        Directory.CreateDirectory(Path.GetDirectoryName(b)!);
        File.WriteAllText(a, "x");
        File.WriteAllText(b, "y");
        var subject = new SqliteBackupService("sqlite3", (args, _) => (0, "dump of " + args[1]));

        var results = subject.Backup([new("Alpha", a), new("Alpha", b)], Path.Combine(tempFolder, "out"));

        Assert.Multiple(() =>
        {
            Assert.That(results.Select(r => r.BackupFile).Distinct().Count(), Is.EqualTo(2));
            Assert.That(File.ReadAllText(results[0].BackupFile), Does.Contain("a"));
            Assert.That(File.ReadAllText(results[1].BackupFile), Does.Contain("b"));
        });
    }
}
