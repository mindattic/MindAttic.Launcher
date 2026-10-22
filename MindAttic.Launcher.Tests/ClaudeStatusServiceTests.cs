using MindAttic.Launcher.Services;
using Microsoft.Data.Sqlite;
using NUnit.Framework;

namespace MindAttic.Launcher.Tests;

/// <summary>M.LNCH-T44: the pace status is read from the orchestrator's database row, falling back to rl-status.json.</summary>
[TestFixture]
[NonParallelizable] // one test sets MINDATTIC_ORCHESTRATOR_DB, which is process-wide
public class ClaudeStatusServiceTests
{
    private string dir = null!;
    private string DbPath => Path.Combine(dir, "orchestrator.db");
    private string FilePath => Path.Combine(dir, "rl-status.json");

    [SetUp]
    public void SetUp()
    {
        dir = Path.Combine(Path.GetTempPath(), "launcher-rlstatus-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
    }

    [TearDown]
    public void TearDown()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
    }

    private void WriteRow(string json, bool withTable = true)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DbPath, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = withTable
            ? "CREATE TABLE IF NOT EXISTS state (name TEXT PRIMARY KEY, value TEXT NOT NULL, written_at REAL NOT NULL);" +
              "INSERT OR REPLACE INTO state VALUES ('rl-status', $v, 1700000000);"
            : "CREATE TABLE accounts (id TEXT);";
        command.Parameters.AddWithValue("$v", json);
        command.ExecuteNonQuery();
    }

    private static string Status(string stamp, double pct) => $$"""{"percent_used": {{pct}}, "timestamp": "{{stamp}}"}""";

    [Test]
    public void Row_newer_than_file_wins()
    {
        WriteRow(Status("2026-10-22T12:05:00Z", 40));
        File.WriteAllText(FilePath, Status("2026-10-22T12:00:00Z", 10));
        Assert.That(ClaudeStatusService.ReadRateLimitStatus(DbPath, FilePath), Does.Contain("40"));
    }

    [Test]
    public void File_newer_than_row_wins()
    {
        WriteRow(Status("2026-10-22T12:00:00Z", 40));
        File.WriteAllText(FilePath, Status("2026-10-22T12:05:00Z", 10));
        Assert.That(ClaudeStatusService.ReadRateLimitStatus(DbPath, FilePath), Does.Contain("10"));
    }

    [Test]
    public void Falls_back_to_file_without_a_database()
    {
        File.WriteAllText(FilePath, Status("2026-10-22T12:00:00Z", 10));
        Assert.That(ClaudeStatusService.ReadRateLimitStatus(DbPath, FilePath), Does.Contain("10"));
    }

    [Test]
    public void Falls_back_to_file_when_the_database_has_no_state_table()
    {
        WriteRow("", withTable: false);
        File.WriteAllText(FilePath, Status("2026-10-22T12:00:00Z", 10));
        Assert.That(ClaudeStatusService.ReadRateLimitStatus(DbPath, FilePath), Does.Contain("10"));
    }

    [Test]
    public void Reads_the_row_alone_without_a_file()
    {
        WriteRow(Status("2026-10-22T12:00:00Z", 40));
        Assert.That(ClaudeStatusService.ReadRateLimitStatus(DbPath, FilePath), Does.Contain("40"));
    }

    [Test]
    public void Null_when_neither_exists() =>
        Assert.That(ClaudeStatusService.ReadRateLimitStatus(DbPath, FilePath), Is.Null);

    [Test]
    public void Reading_does_not_hold_the_database_open()
    {
        WriteRow(Status("2026-10-22T12:00:00Z", 40));
        ClaudeStatusService.ReadRateLimitStatus(DbPath, FilePath);
        Assert.DoesNotThrow(() => File.Delete(DbPath));
    }

    [Test]
    public void Db_path_honours_the_environment_variable()
    {
        var old = Environment.GetEnvironmentVariable("MINDATTIC_ORCHESTRATOR_DB");
        try
        {
            Environment.SetEnvironmentVariable("MINDATTIC_ORCHESTRATOR_DB", DbPath);
            Assert.That(ClaudeStatusService.OrchestratorDbPath(dir), Is.EqualTo(DbPath));
        }
        finally { Environment.SetEnvironmentVariable("MINDATTIC_ORCHESTRATOR_DB", old); }
    }

    [Test]
    public void Db_path_is_the_checkouts_when_it_exists_else_appdata()
    {
        var old = Environment.GetEnvironmentVariable("MINDATTIC_ORCHESTRATOR_DB");
        try
        {
            Environment.SetEnvironmentVariable("MINDATTIC_ORCHESTRATOR_DB", null);
            Assert.That(ClaudeStatusService.OrchestratorDbPath(dir), Does.StartWith(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)));
            Directory.CreateDirectory(Path.Combine(dir, "MindAttic.Orchestrator"));
            Assert.That(ClaudeStatusService.OrchestratorDbPath(dir),
                Is.EqualTo(Path.Combine(dir, "MindAttic.Orchestrator", "data", "orchestrator.db")));
        }
        finally { Environment.SetEnvironmentVariable("MINDATTIC_ORCHESTRATOR_DB", old); }
    }
}
