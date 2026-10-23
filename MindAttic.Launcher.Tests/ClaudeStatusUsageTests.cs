using MindAttic.Launcher.Services;
using NUnit.Framework;

namespace MindAttic.Launcher.Tests;

/// <summary>M.LNCH-T11: ClaudeStatusService's usage parsing, stats-cache fallback, rate-limit line and formatting.</summary>
[TestFixture]
[NonParallelizable] // sets MINDATTIC_ORCHESTRATOR_DB, which is process-wide
public class ClaudeStatusUsageTests
{
    private string dir = null!;
    private string? oldDb;
    private ClaudeStatusService svc = null!;

    [SetUp]
    public void SetUp()
    {
        dir = Path.Combine(Path.GetTempPath(), "launcher-status-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "projects", "p1"));
        oldDb = Environment.GetEnvironmentVariable("MINDATTIC_ORCHESTRATOR_DB");
        Environment.SetEnvironmentVariable("MINDATTIC_ORCHESTRATOR_DB", Path.Combine(dir, "none.db"));
        svc = new ClaudeStatusService(dir);
    }

    [TearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable("MINDATTIC_ORCHESTRATOR_DB", oldDb);
        try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
    }

    private static string Line(string id, string? model, int i, int o, int cc, int cr, string type = "assistant")
    {
        var m = model is null ? "" : $"\"model\":\"{model}\",";
        return $$$$"""{"type":"{{{{type}}}}","message":{"id":"{{{{id}}}}",{{{{m}}}}"usage":{"input_tokens":{{{{i}}}},"output_tokens":{{{{o}}}},"cache_creation_input_tokens":{{{{cc}}}},"cache_read_input_tokens":{{{{cr}}}}}}}""";
    }

    private string WriteJsonl(string name, IEnumerable<string> lines, DateTime? written = null)
    {
        var path = Path.Combine(dir, "projects", "p1", name);
        File.WriteAllLines(path, lines);
        if (written is not null) File.SetLastWriteTime(path, written.Value);
        return path;
    }

    private void WriteStats(string json) => File.WriteAllText(Path.Combine(dir, "stats-cache.json"), json);

    [Test]
    public void Sums_all_four_token_kinds_per_model_and_dedupes_by_message_id()
    {
        WriteJsonl("a.jsonl",
        [
            Line("m1", "claude-opus-4-8", 1, 2, 3, 4),
            Line("m1", "claude-opus-4-8", 1, 2, 3, 4), // duplicate chunk
            Line("m2", "claude-opus-4-8", 10, 0, 0, 0),
            Line("m3", "claude-sonnet-5-5", 100, 200, 0, 0),
        ]);
        var line = svc.BuildUsageLine();
        Assert.That(line, Does.Contain("today"));
        Assert.That(line, Does.Contain("Opus 4.8: [cyan1]20 "));   // 10 + 10
        Assert.That(line, Does.Contain("Sonnet 5.5: [cyan1]300"));
    }

    [Test]
    public void Skips_non_assistant_no_model_and_malformed_lines()
    {
        WriteJsonl("a.jsonl",
        [
            Line("u1", "claude-opus-4-8", 500, 0, 0, 0, type: "user"),
            Line("n1", null, 500, 0, 0, 0),
            "{ this \"type\":\"assistant\" \"usage\" is not json",
            Line("ok", "claude-opus-4-8", 7, 0, 0, 0),
        ]);
        var line = svc.BuildUsageLine();
        Assert.That(line, Does.Contain("Opus 4.8: [cyan1]7[/]"));
    }

    [Test]
    public void Ignores_files_not_written_today_and_falls_back_to_stats_cache()
    {
        WriteJsonl("old.jsonl", [Line("m1", "claude-opus-4-8", 999, 0, 0, 0)], DateTime.Today.AddDays(-2));
        WriteStats("""{"dailyModelTokens":[{"date":"2020-01-01","tokensByModel":{"claude-haiku-4-5-20251001":1}},{"date":"2026-01-02","tokensByModel":{"claude-sonnet-4-6":5000}}]}""");
        var line = svc.BuildUsageLine();
        Assert.That(line, Does.Contain("latest: 2026-01-02"));
        Assert.That(line, Does.Contain("Sonnet 4.6"));
        Assert.That(line, Does.Not.Contain("Opus"));
    }

    [Test]
    public void Stats_cache_dated_today_is_labelled_cached()
    {
        WriteStats($$$"""{"dailyModelTokens":[{"date":"{{{DateTime.Today:yyyy-MM-dd}}}","tokensByModel":{"claude-opus-4-8":42}}]}""");
        Assert.That(svc.BuildUsageLine(), Does.Contain("today (cached)"));
    }

    [Test]
    public void No_data_anywhere_says_so()
    {
        Assert.That(svc.BuildUsageLine(), Does.Contain("no data yet"));
    }

    private string? RateLine(double pct)
    {
        File.WriteAllText(Path.Combine(dir, "rl-status.json"),
            $$"""{"percent_used":{{pct}},"reset_time":"3pm","tokens_remaining":5000,"timestamp":""}""");
        return svc.BuildRateLimitLine();
    }

    [TestCase(74.9, "green", "[ .]")]
    [TestCase(75.0, "yellow", "[ !]")]
    [TestCase(89.9, "yellow", "[ !]")]
    [TestCase(90.0, "red", "[!!]")]
    public void Rate_limit_color_and_icon_thresholds(double pct, string color, string icon)
    {
        var line = RateLine(pct)!;
        Assert.That(line, Does.StartWith($"  [{color}]"));
        Assert.That(line, Does.Contain(icon.Replace("[", "[[").Replace("]", "]]")));
    }

    [Test]
    public void Rate_limit_bar_fill_is_clamped_to_twenty()
    {
        Assert.That(RateLine(250)!, Does.Contain(new string('█', 20)).And.Not.Contain("░"));
        var half = RateLine(50)!;
        Assert.That(half, Does.Contain(new string('█', 10) + new string('░', 10)));
    }

    [Test]
    public void Rate_limit_line_is_null_without_a_status()
    {
        Assert.That(svc.BuildRateLimitLine(), Is.Null);
    }

    [TestCase(0L, "0")]
    [TestCase(999L, "999")]
    [TestCase(1000L, "1.0K")]
    [TestCase(9999L, "10.0K")]
    [TestCase(10000L, "10K")]
    [TestCase(999_999L, "1000K")]
    [TestCase(1_000_000L, "1.0M")]
    public void FormatTokens_boundaries(long n, string expected) =>
        Assert.That(ClaudeStatusService.FormatTokens(n), Is.EqualTo(expected));

    [TestCase("claude-opus-4-8", "Opus 4.8")]
    [TestCase("claude-sonnet-5-5", "Sonnet 5.5")]
    [TestCase("claude-haiku-4-5-20251001", "Haiku 4.5")]
    [TestCase("claude-newmodel-9", "newmodel-9")]
    [TestCase("<synthetic>", "<synthetic>")]
    public void ShortModelName_maps_and_strips_claude_prefix(string model, string expected) =>
        Assert.That(ClaudeStatusService.ShortModelName(model), Is.EqualTo(expected));
}
