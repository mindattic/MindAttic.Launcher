using System.Text.RegularExpressions;
using MindAttic.Launcher.Interop;

namespace MindAttic.Launcher.Services;

/// <summary>
/// Background watchdog that runs inside each <c>mindattic host</c> tab. Every
/// <see cref="PollInterval"/> it peeks the bottom of the console buffer to tell
/// whether the agent is busy (an "esc to interrupt" / "ctrl+c to cancel" prompt
/// is visible, or a background shell it spawned is still running) or idle, then
/// reasserts the tab title as <c>{marker}  {title}</c> — a play glyph while busy,
/// a pause glyph otherwise.
///
/// The reassert is the important part: Claude Code and Codex both rewrite the
/// terminal title with their own OSC sequence while running, which would wipe
/// out our marker. We can't set another tab's title from MindAttic.Launcher
/// (Windows Terminal exposes no such API — only the process *inside* a tab can
/// set that tab's title), so the watchdog has to live here, in-process, one per
/// tab. It reads the current title back via <see cref="ConsoleBuffer.ReadTitle"/>
/// and only rewrites when the CLI has clobbered it, so we win the title back
/// within a quarter second without spamming redundant OSC writes.
///
/// The owning wt tab must be launched without --suppressApplicationTitle for
/// these writes to actually show (see WindowsTerminalLauncher.BuildAgentTab).
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

    // Claude Code's footer shows "N shell" / "N shells" while one or more
    // background shells it spawned are still running. The agent's own prompt is
    // idle in that state (no "esc to interrupt"), but work continues in the
    // background — so a live background shell counts as busy and keeps the play
    // glyph lit. The footer is a controlled context, so the bare word "shell"
    // is signal enough; no need to anchor on the count.
    private static readonly Regex BackgroundShellPattern =
        new(@"\bshells?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // 250 ms keeps the watchdog tight: when the CLI clobbers the title with its
    // own OSC write, we restore the marker within a quarter second instead of
    // the old half-second window where the CLI's bare title showed through.
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    private readonly string title;
    private readonly CancellationTokenSource cts = new();
    private readonly Task loop;

    public TitlePinner(string title)
    {
        this.title = title;
        // System.Console.Title can throw on redirected stdout (CI, piped runs) or on
        // hosts without a real console. Match the loop's swallow-and-continue
        // posture so a non-conhost environment doesn't kill the host command.
        // Seed with the idle marker so the glyph is present from the first frame.
        try { System.Console.Title = Compose(isBusy: false, title); } catch { }
        loop = Task.Run(() => RunLoop(cts.Token));
    }

    /// <summary>
    /// True when the visible buffer carries one of the agents' "working" prompts.
    /// Case-insensitive substring match — Claude Code renders "(esc to interrupt)"
    /// and Codex renders "(… • esc to interrupt)", so the same patterns cover both.
    /// </summary>
    public static bool LooksBusy(string? buffer)
    {
        if (string.IsNullOrEmpty(buffer)) return false;
        var lower = buffer.ToLowerInvariant();
        foreach (var pattern in BusyPatterns)
            if (lower.Contains(pattern)) return true;
        return false;
    }

    /// <summary>
    /// True when Claude Code's footer shows a background shell it spawned is
    /// still running ("1 shell", "2 shells", …). The agent prompt is idle in
    /// that state — <see cref="LooksBusy"/> is false — yet work continues, so
    /// the watchdog keeps the busy/play glyph lit on the title.
    /// </summary>
    public static bool HasBackgroundShell(string? buffer) =>
        !string.IsNullOrEmpty(buffer) && BackgroundShellPattern.IsMatch(buffer);

    /// <summary>Builds the pinned title: <c>{marker}  {title}</c> (two spaces).</summary>
    public static string Compose(bool isBusy, string title) =>
        $"{(isBusy ? BusyMarker : IdleMarker)}  {title}";

    private void RunLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                // Busy = the agent is actively thinking (esc-to-interrupt) OR a
                // background shell it spawned is still running. Either keeps the
                // play glyph lit; only a truly idle prompt falls back to pause.
                var buffer = ConsoleBuffer.ReadBottomRows(20);
                var desired = Compose(LooksBusy(buffer) || HasBackgroundShell(buffer), title);
                // Only rewrite when the title has drifted from what we want — the
                // CLI overwriting it, or a busy/idle transition. Skipping the no-op
                // write avoids needless OSC churn (and the title flicker it causes
                // in Windows Terminal) when nothing has changed.
                if (!string.Equals(ConsoleBuffer.ReadTitle(), desired, StringComparison.Ordinal))
                    System.Console.Title = desired;
            }
            catch
            {
                // Title-buffer reads can race with the host console resizing
                // or closing. Don't take the agent down with us — just loop.
            }

            try { Task.Delay(PollInterval, ct).Wait(ct); }
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
