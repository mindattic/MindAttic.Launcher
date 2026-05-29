using MindAttic.Console.Interop;

namespace MindAttic.Console.Services;

/// <summary>
/// Background loop that polls the bottom of the console buffer every 500 ms
/// and prefixes the tab title with a play glyph while an "esc to interrupt" /
/// "ctrl+c to cancel" prompt is visible (the agent is busy) and a pause glyph
/// otherwise (idle/waiting). The owning wt tab must be launched without
/// --suppressApplicationTitle for the prefix to actually show.
/// </summary>
public sealed class TitlePinner : IDisposable
{
    // The console title is set via the wide SetConsoleTitleW path, so Unicode
    // renders fine in Windows Terminal; "||" / ">" are the ASCII fallbacks if a
    // host ever mangles the glyphs. Title space is tight, so a single glyph
    // stands in for the old "Paused" word.
    // U+23F8 (⏸) DOUBLE VERTICAL BAR — shown when the agent is idle/waiting.
    private const string IdleMarker = "⏸";
    // U+25B6 (▶) BLACK RIGHT-POINTING TRIANGLE — shown while the agent is busy.
    private const string BusyMarker = "▶";
    private static readonly string[] BusyPatterns =
    [
        "esc to interrupt",
        "esc to cancel",
        "ctrl+c to interrupt",
        "ctrl+c to cancel"
    ];

    private readonly string title;
    private readonly CancellationTokenSource cts = new();
    private readonly Task loop;

    public TitlePinner(string title)
    {
        this.title = title;
        // System.Console.Title can throw on redirected stdout (CI, piped runs) or on
        // hosts without a real console. Match the loop's swallow-and-continue
        // posture so a non-conhost environment doesn't kill the host command.
        try { System.Console.Title = title; } catch { }
        loop = Task.Run(() => RunLoop(cts.Token));
    }

    private void RunLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var buf = ConsoleBuffer.ReadBottomRows(20);
                var isBusy = false;
                if (!string.IsNullOrEmpty(buf))
                {
                    var lower = buf.ToLowerInvariant();
                    foreach (var pattern in BusyPatterns)
                    {
                        if (lower.Contains(pattern)) { isBusy = true; break; }
                    }
                }
                var marker = isBusy ? BusyMarker : IdleMarker;
                System.Console.Title = $"{marker}  {title}";
            }
            catch
            {
                // Title-buffer reads can race with the host console resizing
                // or closing. Don't take the agent down with us — just loop.
            }

            try { Task.Delay(500, ct).Wait(ct); }
            catch (OperationCanceledException) { return; }
        }
    }

    public void Dispose()
    {
        cts.Cancel();
        try { loop.Wait(TimeSpan.FromSeconds(1)); } catch { }
        cts.Dispose();
        try { System.Console.Title = $"{title} - Exited"; } catch { }
    }
}
