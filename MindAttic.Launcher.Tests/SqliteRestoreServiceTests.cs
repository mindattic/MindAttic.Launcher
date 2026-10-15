using MindAttic.Launcher.Services;
using NUnit.Framework;

namespace MindAttic.Launcher.Tests;

[TestFixture]
public sealed class SqliteRestoreServiceTests
{
    private string tempFolder = "";

    [SetUp]
    public void SetUp()
    {
        tempFolder = Path.Combine(Path.GetTempPath(), "MindAtticSqliteRestoreTests", TestContext.CurrentContext.Test.ID);
        Directory.CreateDirectory(tempFolder);
    }

    [TearDown]
    public void TearDown()
    {
        try { if (Directory.Exists(tempFolder)) Directory.Delete(tempFolder, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    [Test]
    public void RestoreOne_fails_without_invoking_sqlite3_when_the_dump_file_is_missing()
    {
        var invoked = false;
        var subject = new SqliteRestoreService("sqlite3", (_, _, _) => { invoked = true; return (0, ""); });
        var dbFile = Path.Combine(tempFolder, "app.db");

        var result = subject.RestoreOne("Alpha", dbFile, Path.Combine(tempFolder, "missing.sql"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.False);
            Assert.That(invoked, Is.False);
            Assert.That(result.Output, Does.Contain("not found"));
        });
    }

    [Test]
    public void RestoreOne_deletes_any_existing_file_before_replaying_the_dump()
    {
        var dbFile = Path.Combine(tempFolder, "app.db");
        File.WriteAllText(dbFile, "stale");
        var dumpFile = Path.Combine(tempFolder, "app.sql");
        File.WriteAllText(dumpFile, "CREATE TABLE t(x);");

        string? seenDbFile = null;
        var subject = new SqliteRestoreService("sqlite3", (db, sql, _) =>
        {
            // The runner is invoked after the existing file was already removed —
            // a fresh-restore target, never a merge into the stale one.
            seenDbFile = db;
            Assert.That(File.Exists(dbFile), Is.False);
            Assert.That(sql, Does.Contain("CREATE TABLE"));
            return (0, "");
        });

        var result = subject.RestoreOne("Alpha", dbFile, dumpFile);

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.True);
            Assert.That(seenDbFile, Is.EqualTo(dbFile));
        });
    }

    [Test]
    public void RestoreOne_creates_the_destination_directory_when_missing()
    {
        var dbFile = Path.Combine(tempFolder, "nested", "app.db");
        var dumpFile = Path.Combine(tempFolder, "app.sql");
        File.WriteAllText(dumpFile, "CREATE TABLE t(x);");
        var subject = new SqliteRestoreService("sqlite3", (_, _, _) => (0, ""));

        var result = subject.RestoreOne("Alpha", dbFile, dumpFile);

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.True);
            Assert.That(Directory.Exists(Path.GetDirectoryName(dbFile)), Is.True);
        });
    }

    [Test]
    public void RestoreOne_reports_failure_and_cleans_up_a_partial_file_on_nonzero_exit()
    {
        var dbFile = Path.Combine(tempFolder, "app.db");
        var dumpFile = Path.Combine(tempFolder, "app.sql");
        File.WriteAllText(dumpFile, "CREATE TABLE t(x);");
        var subject = new SqliteRestoreService("sqlite3", (db, _, _) =>
        {
            File.WriteAllText(db, "partial");
            return (1, "syntax error");
        });

        var result = subject.RestoreOne("Alpha", dbFile, dumpFile);

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.False);
            Assert.That(result.ExitCode, Is.EqualTo(1));
            Assert.That(File.Exists(dbFile), Is.False, "a partial restore should not be left behind");
        });
    }

    [Test]
    public void RestoreOne_turns_a_thrown_exception_into_a_failed_result_instead_of_throwing()
    {
        var dbFile = Path.Combine(tempFolder, "app.db");
        var dumpFile = Path.Combine(tempFolder, "app.sql");
        File.WriteAllText(dumpFile, "CREATE TABLE t(x);");
        var subject = new SqliteRestoreService("sqlite3", (_, _, _) => throw new InvalidOperationException("sqlite3 missing"));

        var result = subject.RestoreOne("Alpha", dbFile, dumpFile);

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.False);
            Assert.That(result.Output, Does.Contain("sqlite3 missing"));
        });
    }
}
