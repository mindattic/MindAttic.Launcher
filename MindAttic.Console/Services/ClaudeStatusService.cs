using System.Text.Json;
using Spectre.Console;

namespace MindAttic.Console.Services;

/// <summary>
/// Reads Claude Code usage data from ~/.claude/* and renders a status block
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

        if (usage.Count == 0)
            return "  [grey50]Claude usage: no data yet[/]";

        var parts = usage
            .OrderByDescending(kv => kv.Value)
            .Take(3)
            .Select(kv => $"{ShortModelName(kv.Key)}: [cyan1]{FormatTokens(kv.Value)}[/]");

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

                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (!root.TryGetProperty("message", out var msg)) continue;
                if (!msg.TryGetProperty("usage", out var u)) continue;

                if (msg.TryGetProperty("id", out var idProp))
                {
                    var id = idProp.GetString() ?? "";
                    if (!string.IsNullOrEmpty(id) && !seen.Add(id)) continue;
                }

                var model = msg.TryGetProperty("model", out var mp) ? mp.GetString() ?? "" : "";
                if (string.IsNullOrEmpty(model)) continue;

                long total =
                    (u.TryGetProperty("input_tokens",               out var inp)  ? inp.GetInt64()  : 0) +
                    (u.TryGetProperty("output_tokens",              out var outp) ? outp.GetInt64() : 0) +
                    (u.TryGetProperty("cache_creation_input_tokens",out var cc)   ? cc.GetInt64()   : 0) +
                    (u.TryGetProperty("cache_read_input_tokens",    out var cr)   ? cr.GetInt64()   : 0);

                usage[model] = usage.GetValueOrDefault(model) + total;
            }
        }
        catch { /* skip malformed or locked files */ }
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

    private string? BuildRateLimitLine()
    {
        var rlFile = Path.Combine(ClaudeDir, "rl-status.json");
        if (!File.Exists(rlFile)) return null;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(rlFile));
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
