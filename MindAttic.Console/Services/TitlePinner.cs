using MindAttic.Console.Interop;

namespace MindAttic.Console.Services;

/// <summary>
/// Background loop that polls the bottom of the console buffer every 500 ms
/// and prefixes the tab title with a pause glyph when no "esc to interrupt" /
/// "ctrl+c to cancel" prompt is visible. The owning wt tab must be launched
/// without --suppressApplicationTitle for the prefix to actually show.
/// </summary>
public sealed class TitlePinner : IDisposable
{
    // U+23F8 (⏸) DOUBLE VERTICAL BAR. The console title is set via the wide
    // SetConsoleTitleW path, so Unicode renders fine in Windows Terminal; "||"
    // is the ASCII fallback if a host ever mangles the glyph. Title space is
    // tight, so this replaces the old word "Paused".
    private const string IdleMarker = "⏸";
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
                System.Console.Title = isBusy ? title : $"{IdleMarker}  {title}";
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
