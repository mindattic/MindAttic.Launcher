using MindAttic.Launcher.Services;
using NUnit.Framework;

namespace MindAttic.Launcher.Tests;

[TestFixture]
public sealed class SqlRestoreServiceTests
{
    [TestCase(@"R:\Backup\MindAttic\2026-05-23\Databases\localhost\Legion.bak", ExpectedResult = "Legion")]
    [TestCase(@"C:\x\My]Db.bak", ExpectedResult = "My]Db")]
    public string DeriveDatabaseName_is_the_file_stem(string bakFile) =>
        SqlRestoreService.DeriveDatabaseName(bakFile);

    [Test]
    public void ParseFileList_parses_pipe_separated_rows_and_skips_the_row_count_footer()
    {
        var output = "MyDb              |C:\\Data\\MyDb.mdf  |D|PRIMARY    |...\n" +
                     "MyDb_log          |C:\\Logs\\MyDb.ldf  |L|NULL       |...\n" +
                     "(2 rows affected)\n";

        var entries = SqlRestoreService.ParseFileList(output);

        Assert.That(entries, Has.Count.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(entries[0].LogicalName, Is.EqualTo("MyDb"));
            Assert.That(entries[0].Type, Is.EqualTo("D"));
            Assert.That(entries[1].LogicalName, Is.EqualTo("MyDb_log"));
            Assert.That(entries[1].Type, Is.EqualTo("L"));
        });
    }

    [Test]
    public void ParseFileList_ignores_blank_and_malformed_lines()
    {
        var entries = SqlRestoreService.ParseFileList("\n   \nonly|two\n");
        Assert.That(entries, Is.Empty);
    }

    [Test]
    public void FirstDataLine_skips_blank_lines_and_the_rows_affected_footer()
    {
        Assert.That(SqlRestoreService.FirstDataLine("\n   \nC:\\Data\\\n(1 rows affected)\n"),
            Is.EqualTo("C:\\Data\\"));
    }

    [Test]
    public void BuildRestoreSql_moves_a_single_data_and_log_file_to_the_mdf_ldf_names()
    {
        var fileList = new[]
        {
            new BakFileEntry("Legion", "C:\\Old\\Legion.mdf", "D"),
            new BakFileEntry("Legion_log", "C:\\Old\\Legion.ldf", "L"),
        };
        var paths = new SqlDefaultPaths(@"C:\SQL\Data", @"C:\SQL\Log");

        var sql = SqlRestoreService.BuildRestoreSql("Legion", @"R:\Backup\Legion.bak", fileList, paths);

        Assert.Multiple(() =>
        {
            Assert.That(sql, Does.Contain("RESTORE DATABASE [Legion]"));
            Assert.That(sql, Does.Contain(@"FROM DISK = N'R:\Backup\Legion.bak'"));
            Assert.That(sql, Does.Contain("WITH REPLACE, RECOVERY"));
            Assert.That(sql, Does.Contain(@"MOVE N'Legion' TO N'C:\SQL\Data\Legion.mdf'"));
            Assert.That(sql, Does.Contain(@"MOVE N'Legion_log' TO N'C:\SQL\Log\Legion.ldf'"));
        });
    }

    [Test]
    public void BuildRestoreSql_numbers_a_second_data_and_log_file_as_ndf_and_suffixed_ldf()
    {
        var fileList = new[]
        {
            new BakFileEntry("Legion", "C:\\Old\\Legion.mdf", "D"),
            new BakFileEntry("Legion2", "C:\\Old\\Legion2.ndf", "D"),
            new BakFileEntry("Legion_log", "C:\\Old\\Legion.ldf", "L"),
            new BakFileEntry("Legion_log2", "C:\\Old\\Legion2.ldf", "L"),
        };
        var paths = new SqlDefaultPaths(@"C:\SQL\Data", @"C:\SQL\Log");

        var sql = SqlRestoreService.BuildRestoreSql("Legion", @"R:\Backup\Legion.bak", fileList, paths);

        Assert.Multiple(() =>
        {
            Assert.That(sql, Does.Contain(@"MOVE N'Legion' TO N'C:\SQL\Data\Legion.mdf'"));
            Assert.That(sql, Does.Contain(@"MOVE N'Legion2' TO N'C:\SQL\Data\Legion2.ndf'"));
            Assert.That(sql, Does.Contain(@"MOVE N'Legion_log' TO N'C:\SQL\Log\Legion.ldf'"));
            Assert.That(sql, Does.Contain(@"MOVE N'Legion_log2' TO N'C:\SQL\Log\Legion2.ldf'"));
        });
    }

    [Test]
    public void BuildRestoreSql_escapes_brackets_in_db_name_and_quotes_in_paths()
    {
        var fileList = new[] { new BakFileEntry("a'b", "C:\\Old\\a.mdf", "D") };
        var paths = new SqlDefaultPaths(@"C:\SQL\Data", @"C:\SQL\Log");

        var sql = SqlRestoreService.BuildRestoreSql("My]Db", @"R:\a'b\My]Db.bak", fileList, paths);

        Assert.Multiple(() =>
        {
            Assert.That(sql, Does.Contain("RESTORE DATABASE [My]]Db]"));
            Assert.That(sql, Does.Contain(@"FROM DISK = N'R:\a''b\My]Db.bak'"));
            Assert.That(sql, Does.Contain("MOVE N'a''b'"));
        });
    }

    [Test]
    public void GetDefaultPaths_falls_back_to_data_path_when_log_query_fails()
    {
        var subject = new SqlRestoreService("sqlcmd", (args, _) =>
            args[^1].Contains("InstanceDefaultDataPath") ? (0, @"C:\SQL\Data\") : (1, ""));

        var paths = subject.GetDefaultPaths("localhost");

        Assert.That(paths, Is.EqualTo(new SqlDefaultPaths(@"C:\SQL\Data", @"C:\SQL\Data")));
    }

    [Test]
    public void GetDefaultPaths_throws_when_data_path_query_returns_empty()
    {
        var subject = new SqlRestoreService("sqlcmd", (_, _) => (0, ""));

        Assert.Throws<InvalidOperationException>(() => subject.GetDefaultPaths("localhost"));
    }

    [Test]
    public void RestoreOne_moves_existing_database_to_single_user_before_restoring()
    {
        var calls = new List<string>();
        var subject = new SqlRestoreService("sqlcmd", (args, _) =>
        {
            var sql = args[^1];
            calls.Add(sql);
            if (sql.Contains("FILELISTONLY"))
                return (0, "Legion|C:\\Old\\Legion.mdf|D|PRIMARY|...\n(1 rows affected)\n");
            if (sql.Contains("InstanceDefaultDataPath")) return (0, @"C:\SQL\Data\");
            if (sql.Contains("InstanceDefaultLogPath")) return (0, @"C:\SQL\Log\");
            if (sql.Contains("sys.databases")) return (0, "Legion\n(1 rows affected)\n");
            return (0, "");
        });

        var result = subject.RestoreOne("localhost", @"R:\Backup\Legion.bak");

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.True);
            Assert.That(result.Database, Is.EqualTo("Legion"));
            Assert.That(calls.Any(c => c.Contains("SINGLE_USER")), Is.True,
                "an existing database must be dropped to SINGLE_USER before RESTORE");
            Assert.That(calls.Any(c => c.Contains("RESTORE DATABASE")), Is.True);
        });
    }

    [Test]
    public void RestoreOne_skips_single_user_when_the_database_does_not_exist()
    {
        var calls = new List<string>();
        var subject = new SqlRestoreService("sqlcmd", (args, _) =>
        {
            var sql = args[^1];
            calls.Add(sql);
            if (sql.Contains("FILELISTONLY"))
                return (0, "Legion|C:\\Old\\Legion.mdf|D|PRIMARY|...\n(1 rows affected)\n");
            if (sql.Contains("InstanceDefaultDataPath")) return (0, @"C:\SQL\Data\");
            if (sql.Contains("InstanceDefaultLogPath")) return (0, @"C:\SQL\Log\");
            if (sql.Contains("sys.databases")) return (0, ""); // not found
            return (0, "");
        });

        var result = subject.RestoreOne("localhost", @"R:\Backup\Legion.bak");

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.True);
            Assert.That(calls.Any(c => c.Contains("SINGLE_USER")), Is.False);
        });
    }

    [Test]
    public void RestoreOne_reports_failure_without_throwing_when_restore_exits_nonzero()
    {
        var subject = new SqlRestoreService("sqlcmd", (args, _) =>
        {
            var sql = args[^1];
            if (sql.Contains("FILELISTONLY"))
                return (0, "Legion|C:\\Old\\Legion.mdf|D|PRIMARY|...\n(1 rows affected)\n");
            if (sql.Contains("InstanceDefaultDataPath")) return (0, @"C:\SQL\Data\");
            if (sql.Contains("InstanceDefaultLogPath")) return (0, @"C:\SQL\Log\");
            if (sql.Contains("sys.databases")) return (0, "");
            if (sql.Contains("RESTORE DATABASE")) return (3, "Msg 3201: Cannot open backup device.");
            return (0, "");
        });

        var result = subject.RestoreOne("localhost", @"R:\Backup\Legion.bak");

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.False);
            Assert.That(result.ExitCode, Is.EqualTo(3));
            Assert.That(result.Output, Does.Contain("3201"));
        });
    }

    [Test]
    public void RestoreOne_turns_a_thrown_exception_into_a_failed_result_instead_of_throwing()
    {
        var subject = new SqlRestoreService("sqlcmd", (_, _) => throw new InvalidOperationException("sqlcmd missing"));

        var result = subject.RestoreOne("localhost", @"R:\Backup\Legion.bak");

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.False);
            Assert.That(result.Output, Does.Contain("sqlcmd missing"));
        });
    }
}
