using System.Text.Json;
using Spectre.Console;

namespace MindAttic.Launcher.Services;

/// <summary>
/// Reads Claude Code usage data from ~/.claude/* (and the orchestrator's pace
/// status from its database, see <see cref="ReadRateLimitStatus"/>) and renders a status block
/// for the main menu. Results are cached for 60 seconds to avoid thrashing
/// the filesystem on every redraw.
/// </summary>
public sealed class ClaudeStatusService
{
    private static readonly string ClaudeDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");

    private string[] _cachedLines = [];
    private DateTime _lastRefresh = DateTime.MinValue;

    public void Render()
    {
        if (DateTime.UtcNow - _lastRefresh >= TimeSpan.FromMinutes(1))
        {
            _cachedLines = BuildLines();
            _lastRefresh = DateTime.UtcNow;
        }
        foreach (var line in _cachedLines)
            AnsiConsole.MarkupLine(line);
        AnsiConsole.WriteLine();
    }

    private string[] BuildLines()
    {
        var lines = new List<string> { BuildUsageLine() };
        var rl = BuildRateLimitLine();
        if (rl is not null)
            lines.Add(rl);
        return [.. lines];
    }

    // ── Usage line ───────────────────────────────────────────────────────────

    private string BuildUsageLine()
    {
        var usage = ReadTodayFromJsonl();
        string label;

        if (usage.Count > 0)
        {
            label = "today";
        }
        else
        {
            usage = ReadFromStatsCache(out var cacheDate);
            label = cacheDate is null
                ? "latest"
                : cacheDate == DateTime.Today.ToString("yyyy-MM-dd")
                    ? "today (cached)"
                    : $"latest: {cacheDate}";
        }

        // Entries like "<synthetic>" carry no real token usage — drop anything
        // that rounds to zero so the line doesn't pad itself out with noise.
        var shown = usage
            .Where(kv => kv.Value > 0)
            .OrderByDescending(kv => kv.Value)
            .Take(3)
            .Select(kv => (Name: ShortModelName(kv.Key), Tokens: FormatTokens(kv.Value)))
            .ToList();

        if (shown.Count == 0)
            return "  [grey50]Claude usage: no data yet[/]";

        // Pad every count to the widest one shown so they share a left edge
        // instead of drifting with each model's digit count.
        var width = shown.Max(s => s.Tokens.Length);
        var parts = shown.Select(s => $"{s.Name}: [cyan1]{s.Tokens.PadRight(width)}[/]");

        return $"  [grey50]{label}[/]  " + string.Join("  [grey50]·[/]  ", parts);
    }

    private Dictionary<string, long> ReadTodayFromJsonl()
    {
        var usage = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var projectsDir = new DirectoryInfo(Path.Combine(ClaudeDir, "projects"));
        if (!projectsDir.Exists) return usage;

        try
        {
            var today = DateTime.Today;
            foreach (var projectDir in projectsDir.GetDirectories())
            {
                foreach (var file in projectDir.GetFiles("*.jsonl"))
                {
                    if (file.LastWriteTime.Date != today) continue;
                    ParseJsonlUsage(file.FullName, usage);
                }
            }
        }
        catch { /* non-fatal — stale or missing project dirs */ }

        return usage;
    }

    private static void ParseJsonlUsage(string path, Dictionary<string, long> usage)
    {
        // Each API response may appear as multiple JSONL entries (thinking /
        // text / tool_use streaming chunks all carry the same message id).
        // Deduplicate by id so we don't triple-count a single round-trip.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            foreach (var line in File.ReadLines(path))
            {
                if (!line.Contains("\"type\":\"assistant\"", StringComparison.Ordinal)) continue;
                if (!line.Contains("\"usage\"", StringComparison.Ordinal)) continue;

                // One malformed or odd-typed line skips only itself, not the rest of the file.
                try { AddLineUsage(line, seen, usage); }
                catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException) { }
            }
        }
        catch { /* skip locked or unreadable files */ }
    }

    private static void AddLineUsage(string line, HashSet<string> seen, Dictionary<string, long> usage)
    {
        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;
        if (!root.TryGetProperty("message", out var msg)) return;
        if (!msg.TryGetProperty("usage", out var u)) return;

        if (msg.TryGetProperty("id", out var idProp))
        {
            var id = idProp.GetString() ?? "";
            if (!string.IsNullOrEmpty(id) && !seen.Add(id)) return;
        }

        var model = msg.TryGetProperty("model", out var mp) ? mp.GetString() ?? "" : "";
        if (string.IsNullOrEmpty(model)) return;

        long total =
            (u.TryGetProperty("input_tokens",               out var inp)  ? inp.GetInt64()  : 0) +
            (u.TryGetProperty("output_tokens",              out var outp) ? outp.GetInt64() : 0) +
            (u.TryGetProperty("cache_creation_input_tokens",out var cc)   ? cc.GetInt64()   : 0) +
            (u.TryGetProperty("cache_read_input_tokens",    out var cr)   ? cr.GetInt64()   : 0);

        usage[model] = usage.GetValueOrDefault(model) + total;
    }

    private static Dictionary<string, long> ReadFromStatsCache(out string? cacheDate)
    {
        cacheDate = null;
        var usage = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var statsFile = Path.Combine(ClaudeDir, "stats-cache.json");
        if (!File.Exists(statsFile)) return usage;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(statsFile));
            var root = doc.RootElement;
            if (!root.TryGetProperty("dailyModelTokens", out var daily)) return usage;

            var entries = daily.EnumerateArray().ToList();
            if (entries.Count == 0) return usage;

            var last = entries[^1];
            cacheDate = last.TryGetProperty("date", out var dp) ? dp.GetString() : null;
            if (!last.TryGetProperty("tokensByModel", out var byModel)) return usage;

            foreach (var model in byModel.EnumerateObject())
                usage[model.Name] = model.Value.GetInt64();
        }
        catch { /* ignore malformed cache */ }

        return usage;
    }

    // ── Rate limit line ──────────────────────────────────────────────────────

    /// <summary>The orchestrator's database, found as its <c>OrchestratorDb.StatePath</c> finds it (M.ORCH-T526):
    /// <c>MINDATTIC_ORCHESTRATOR_DB</c> when set, else the main checkout's <c>MindAttic.Orchestrator\data\orchestrator.db</c>
    /// under the default workspace when that checkout exists, else the machine-local one under <c>%APPDATA%</c>.</summary>
    public static string OrchestratorDbPath(string? workspaceRoot = null)
    {
        if (Environment.GetEnvironmentVariable("MINDATTIC_ORCHESTRATOR_DB") is { Length: > 0 } env) return env;
        var checkout = Path.Combine(workspaceRoot ?? MindAttic.Tickets.Workspace.DefaultRoot, "MindAttic.Orchestrator");
        return Directory.Exists(checkout)
            ? Path.Combine(checkout, "data", "orchestrator.db")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MindAttic", "MindAttic.Orchestrator", "orchestrator.db");
    }

    /// <summary>The <c>state</c> row the orchestrator keeps its pace status in.</summary>
    public const string RateLimitStateName = "rl-status";

    /// <summary>
    /// The orchestrator's pace status as last written (M.LNCH-T44): the <c>rl-status</c> row of its database's <c>state</c>
    /// table or <c>~/.claude/rl-status.json</c> (which it keeps exporting for readers that haven't switched), whichever has
    /// the newer timestamp; null when neither can be read. The database is opened read-only with a short busy timeout, so
    /// a write in progress costs at most a moment and never blocks the menu.
    /// </summary>
    public static string? ReadRateLimitStatus(string dbPath, string filePath)
    {
        var row = ReadStateRow(dbPath, RateLimitStateName);
        (string Json, DateTime WrittenUtc)? file = null;
        try
        {
            if (File.Exists(filePath))
                file = (File.ReadAllText(filePath), File.GetLastWriteTimeUtc(filePath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }

        if (row is null || file is null) return row?.Json ?? file?.Json;
        return Stamp(row.Value.Json, row.Value.WrittenUtc) >= Stamp(file.Value.Json, file.Value.WrittenUtc)
            ? row.Value.Json : file.Value.Json;

        // The status's own timestamp; when it has none, when its row or file was written.
        static DateTime Stamp(string json, DateTime written)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("timestamp", out var t) && t.ValueKind == JsonValueKind.String &&
                    DateTimeOffset.TryParse(t.GetString(), System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed))
                    return parsed.UtcDateTime;
            }
            catch (JsonException) { }
            return written;
        }
    }

    private static (string Json, DateTime WrittenUtc)? ReadStateRow(string dbPath, string name)
    {
        if (!File.Exists(dbPath)) return null;
        try
        {
            using var connection = new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
            {
                DataSource = dbPath,
                Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly,
                // Not pooled: a held handle would keep the orchestrator's sync from committing or replacing the file.
                Pooling = false,
                DefaultTimeout = 1,
            }.ToString());
            connection.Open();
            using (var pragma = connection.CreateCommand())
            {
                pragma.CommandText = "PRAGMA busy_timeout = 250;";
                pragma.ExecuteNonQuery();
            }
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT value, written_at FROM state WHERE name = $name;";
            command.Parameters.AddWithValue("$name", name);
            using var r = command.ExecuteReader();
            if (!r.Read() || r.IsDBNull(0)) return null;
            var written = r.IsDBNull(1) ? DateTime.MinValue
                : DateTime.UnixEpoch.AddTicks((long)(r.GetDouble(1) * TimeSpan.TicksPerSecond));
            return (r.GetString(0), written);
        }
        // Busy, locked, an older schema without the table, or a file that isn't a database: fall back to the JSON file.
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or IOException or UnauthorizedAccessException
                                       or InvalidOperationException or InvalidCastException or FormatException) { return null; }
    }

    private string? BuildRateLimitLine()
    {
        var json = ReadRateLimitStatus(OrchestratorDbPath(), Path.Combine(ClaudeDir, "rl-status.json"));
        if (json is null) return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            double pct   = root.TryGetProperty("percent_used",      out var p)  ? p.GetDouble()    : 0;
            string reset = root.TryGetProperty("reset_time",        out var r)  ? r.GetString() ?? "?" : "?";
            long   left  = root.TryGetProperty("tokens_remaining",  out var t)  ? t.GetInt64()     : 0;
            string ts    = root.TryGetProperty("timestamp",         out var tsp) ? tsp.GetString() ?? "" : "";

            string color = pct >= 90 ? "red" : pct >= 75 ? "yellow" : "green";
            string icon  = pct >= 90 ? "[!!]" : pct >= 75 ? "[ !]" : "[ .]";

            int    filled = Math.Min(20, (int)(pct / 5));
            string bar    = new string('█', filled) + new string('░', 20 - filled);
            string age    = BuildAge(ts);

            return $"  [{color}]{Markup.Escape(icon)}[/] [grey50]rate limit[/]  " +
                   $"[{color}]{Markup.Escape(bar)}[/]  " +
                   $"[cyan1]{pct:F1}%[/] used · resets [cyan1]{Markup.Escape(reset)}[/] · " +
                   $"[grey50]{FormatTokens(left)} left{age}[/]";
        }
        catch { return null; }
    }

    private static string BuildAge(string ts)
    {
        if (string.IsNullOrEmpty(ts)) return "";
        try
        {
            var parsed = DateTimeOffset.Parse(ts).UtcDateTime;
            var mins = (int)(DateTime.UtcNow - parsed).TotalMinutes;
            return mins < 2 ? " (just now)" : $" (cached {mins}m ago)";
        }
        catch { return ""; }
    }

    // ── Formatting helpers ───────────────────────────────────────────────────

    private static string ShortModelName(string model) => model switch
    {
        "claude-fable-5"              => "Fable 5",
        "claude-opus-4-8"             => "Opus 4.8",
        "claude-opus-4-7"             => "Opus 4.7",
        "claude-opus-4-6"             => "Opus 4.6",
        "claude-sonnet-5-5"           => "Sonnet 5.5",
        "claude-sonnet-5"             => "Sonnet 5",
        "claude-sonnet-4-6"           => "Sonnet 4.6",
        "claude-haiku-4-5-20251001"   => "Haiku 4.5",
        _ when model.StartsWith("claude-") => model["claude-".Length..],
        _ => model
    };

    private static string FormatTokens(long n) => n switch
    {
        >= 1_000_000 => $"{n / 1_000_000.0:F1}M",
        >= 10_000    => $"{n / 1_000.0:F0}K",
        >= 1_000     => $"{n / 1_000.0:F1}K",
        _            => n.ToString()
    };
}
