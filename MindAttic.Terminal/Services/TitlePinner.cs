using MindAttic.Terminal.Interop;

namespace MindAttic.Terminal.Services;

/// <summary>
/// Background loop that polls the bottom of the console buffer every 500 ms
/// and prefixes the tab title with "Paused" when no "esc to interrupt" /
/// "ctrl+c to cancel" prompt is visible. The owning wt tab must be launched
/// without --suppressApplicationTitle for the prefix to actually show.
/// </summary>
public sealed class TitlePinner : IDisposable
{
    private const string IdleMarker = "Paused";
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
        Console.Title = title;
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
                Console.Title = isBusy ? title : $"{IdleMarker} - {title}";
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
        try { Console.Title = $"{title} - Exited"; } catch { }
    }
}
