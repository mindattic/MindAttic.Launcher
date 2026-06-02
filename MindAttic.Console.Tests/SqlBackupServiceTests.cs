using MindAttic.Console.Models;
using MindAttic.Console.Services;
using NUnit.Framework;

namespace MindAttic.Console.Tests;

[TestFixture]
public sealed class SqlBackupServiceTests
{
    private string tempFolder = "";

    [SetUp]
    public void SetUp()
    {
        tempFolder = Path.Combine(Path.GetTempPath(), "MindAtticSqlBackupTests", TestContext.CurrentContext.Test.ID);
        Directory.CreateDirectory(tempFolder);
    }

    [TearDown]
    public void TearDown()
    {
        try { if (Directory.Exists(tempFolder)) Directory.Delete(tempFolder, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    [TestCase(null, ExpectedResult = "localhost")]
    [TestCase("", ExpectedResult = "localhost")]
    [TestCase("   ", ExpectedResult = "localhost")]
    [TestCase(@".\SQLEXPRESS", ExpectedResult = @".\SQLEXPRESS")]
    [TestCase("  myhost  ", ExpectedResult = "myhost")]
    public string ResolveInstance_defaults_blank_to_localhost_and_trims(string? instance) =>
        SqlBackupService.ResolveInstance(instance);

    [Test]
    public void ResolveBackupFilePath_places_bak_under_databases_subfolder()
    {
        Assert.That(SqlBackupService.ResolveBackupFilePath(@"R:\Backup\MindAttic\2026-05-23", "localhost", "Legion"),
            Is.EqualTo(@"R:\Backup\MindAttic\2026-05-23\Databases\localhost\Legion.bak"));
    }

    [Test]
    public void ResolveBackupFilePath_namespaces_by_server_so_same_db_on_two_instances_does_not_collide()
    {
        var a = SqlBackupService.ResolveBackupFilePath(@"R:\Backup", "localhost", "App");
        var b = SqlBackupService.ResolveBackupFilePath(@"R:\Backup", @".\SQLEXPRESS", "App");
        Assert.That(a, Is.Not.EqualTo(b));
    }

    [Test]
    public void ResolveBackupFilePath_scrubs_path_illegal_characters_in_server_and_database()
    {
        var path = SqlBackupService.ResolveBackupFilePath(@"R:\Backup", @".\SQLEXPRESS", "weird:name*db");
        Assert.Multiple(() =>
        {
            Assert.That(Path.GetFileName(path), Is.EqualTo("weird_name_db.bak"));
            // The server segment's backslash is scrubbed (the dot is a legal
            // filename char), so it stays a single folder level rather than
            // nesting under ".\".
            Assert.That(Path.GetFileName(Path.GetDirectoryName(path)), Is.EqualTo("._SQLEXPRESS"));
        });
    }

    [Test]
    public void BuildBackupSql_is_a_full_copy_only_checksummed_backup_with_escaped_identifiers()
    {
        var sql = SqlBackupService.BuildBackupSql("My]Db", @"R:\a'b\My]Db.bak");
        Assert.Multiple(() =>
        {
            // Bracket in the db name is doubled, single quote in the path is doubled.
            Assert.That(sql, Does.Contain("BACKUP DATABASE [My]]Db]"));
            Assert.That(sql, Does.Contain(@"TO DISK = N'R:\a''b\My]Db.bak'"));
            Assert.That(sql, Does.Contain("COPY_ONLY"));
            Assert.That(sql, Does.Contain("CHECKSUM"));
            Assert.That(sql, Does.Contain("FORMAT"));
            Assert.That(sql, Does.Contain("INIT"));
        });
    }

    [Test]
    public void BuildArguments_uses_trusted_auth_trusted_cert_and_error_exit_code()
    {
        var args = SqlBackupService.BuildArguments("localhost", "BACKUP DATABASE [X] TO DISK = N'x';");
        Assert.That(args, Is.EqualTo(new[]
        {
            "-S", "localhost", "-E", "-C", "-b", "-Q", "BACKUP DATABASE [X] TO DISK = N'x';"
        }));
    }

    [TestCase(0, false, ExpectedResult = true)]
    [TestCase(1, false, ExpectedResult = false)]
    [TestCase(-1, false, ExpectedResult = false)]
    [TestCase(0, true, ExpectedResult = false)]
    public bool ComputeOk_only_zero_uncancelled_is_success(int exitCode, bool cancelled) =>
        SqlBackupService.ComputeOk(exitCode, cancelled);

    [Test]
    public void CollectTargets_flattens_projects_defaults_instance_skips_blanks_and_dedupes()
    {
        var settings = new AppSettings
        {
            Projects =
            [
                new Project { Name = "Alpha", SqlServer = @".\SQLEXPRESS", Databases = ["AlphaDb", "  ", "Shared"] },
                new Project { Name = "Beta",  Databases = ["BetaDb"] }, // null SqlServer -> localhost
                new Project { Name = "Gamma", SqlServer = @".\SQLEXPRESS", Databases = ["Shared"] }, // dup of Alpha's
                new Project { Name = "Delta" } // no databases
            ]
        };

        var targets = SqlBackupService.CollectTargets(settings);

        Assert.That(targets, Is.EquivalentTo(new[]
        {
            new BackupTarget(@".\SQLEXPRESS", "AlphaDb"),
            new BackupTarget(@".\SQLEXPRESS", "Shared"),
            new BackupTarget("localhost", "BetaDb")
        }));
    }

    [Test]
    public void CollectTargets_dedupes_case_insensitively_on_server_and_database()
    {
        var settings = new AppSettings
        {
            Projects =
            [
                new Project { Name = "Alpha", SqlServer = "localhost", Databases = ["MyDb"] },
                // Same db, different case + server-host case — one backup, not two
                // that would write over each other's .bak on a case-insensitive FS.
                new Project { Name = "Beta", SqlServer = "LOCALHOST", Databases = ["mydb"] }
            ]
        };

        var targets = SqlBackupService.CollectTargets(settings);

        Assert.That(targets, Has.Count.EqualTo(1));
    }

    [Test]
    public void BackupOne_returns_success_and_creates_the_databases_folder_on_exit_zero()
    {
        IReadOnlyList<string>? captured = null;
        var subject = new SqlBackupService("sqlcmd", (args, _) =>
        {
            captured = args;
            return (0, "");
        });

        var result = subject.BackupOne(new BackupTarget("localhost", "Legion"), tempFolder);

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.True);
            Assert.That(result.ExitCode, Is.EqualTo(0));
            Assert.That(result.Output, Is.Empty);
            Assert.That(result.BackupFile, Is.EqualTo(Path.Combine(tempFolder, "Databases", "localhost", "Legion.bak")));
            Assert.That(Directory.Exists(Path.Combine(tempFolder, "Databases", "localhost")), Is.True);
            Assert.That(captured, Is.Not.Null);
            Assert.That(captured![1], Is.EqualTo("localhost"));
        });
    }

    [Test]
    public void BackupOne_captures_failure_output_without_throwing()
    {
        var subject = new SqlBackupService("sqlcmd", (_, _) => (1, "Msg 3201: Cannot open backup device."));

        var result = subject.BackupOne(new BackupTarget("localhost", "Legion"), tempFolder);

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.False);
            Assert.That(result.ExitCode, Is.EqualTo(1));
            Assert.That(result.Output, Does.Contain("3201"));
        });
    }

    [Test]
    public void BackupOne_gives_a_clear_message_when_sqlcmd_is_not_on_path()
    {
        // ERROR_FILE_NOT_FOUND (2) is what Process.Start surfaces when the exe
        // isn't found — the service should translate it to a plain explanation.
        var subject = new SqlBackupService("sqlcmd",
            (_, _) => throw new System.ComponentModel.Win32Exception(2, "The system cannot find the file specified."));

        var result = subject.BackupOne(new BackupTarget("localhost", "Legion"), tempFolder);

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.False);
            Assert.That(result.Output, Does.Contain("not found on PATH"));
            Assert.That(result.Output, Does.Contain("sqlcmd"));
        });
    }

    [Test]
    public void BackupOne_turns_runner_exception_into_a_failed_result()
    {
        var subject = new SqlBackupService("sqlcmd", (_, _) => throw new InvalidOperationException("sqlcmd missing"));

        var result = subject.BackupOne(new BackupTarget("localhost", "Legion"), tempFolder);

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.False);
            Assert.That(result.ExitCode, Is.EqualTo(-1));
            Assert.That(result.Output, Does.Contain("sqlcmd missing"));
        });
    }

    [Test]
    public void Backup_runs_every_target_and_fires_onDone_for_each()
    {
        var subject = new SqlBackupService("sqlcmd", (_, _) => (0, ""));
        var targets = new[]
        {
            new BackupTarget("localhost", "One"),
            new BackupTarget("localhost", "Two")
        };
        var done = new List<string>();

        var results = subject.Backup(targets, tempFolder, onDone: r => done.Add(r.Database));

        Assert.Multiple(() =>
        {
            Assert.That(results.Select(r => r.Database), Is.EqualTo(new[] { "One", "Two" }));
            Assert.That(results.All(r => r.Ok), Is.True);
            Assert.That(done, Is.EqualTo(new[] { "One", "Two" }));
        });
    }

    [Test]
    public void Backup_stops_before_remaining_targets_when_cancelled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var subject = new SqlBackupService("sqlcmd", (_, _) => (0, ""));
        var targets = new[] { new BackupTarget("localhost", "One"), new BackupTarget("localhost", "Two") };

        var results = subject.Backup(targets, tempFolder, ct: cts.Token);

        Assert.That(results, Is.Empty);
    }
}
