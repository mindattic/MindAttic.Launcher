using System.Diagnostics;
using System.Globalization;

namespace MindAttic.Launcher.Services;

/// <summary>
/// Detects when the running menu binary is older than the latest commit in the
/// Launcher repo. The menu process can't rebuild itself while running
/// (<see cref="ExePath.EnsureFresh"/> only refreshes the artifact used to spawn
/// agent tabs, never the menu process you're navigating), so a stale build
/// silently shows stale menus — a project or menu entry added in a recent commit
/// stays invisible until the Launcher is republished and relaunched. This surfaces
/// a one-line notice at startup so the gap is visible instead of silent.
/// </summary>
public static class BuildFreshness
{
    /// <param name="DaysBehind">Whole days between the build and the latest commit; 0 when behind by less than a day.</param>
    public sealed record Result(int DaysBehind, DateTimeOffset ExeBuilt, DateTimeOffset HeadCommitted);

    /// <summary>
    /// Gathers the real inputs (running exe build time + repo HEAD commit time)
    /// and evaluates staleness. Returns null when the build is current, the exe
    /// path is unknown, the exe lives outside the repo, or git can't be reached
    /// — i.e. "no notice" is always the safe, silent default.
    /// </summary>
    public static Result? Check()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe) || !File.Exists(exe)) return null;

        var root = ExePath.RepoRoot;
        if (root is null) return null;

        var head = LatestCommitTime(root);
        if (head is null) return null;

        var built = new DateTimeOffset(File.GetLastWriteTime(exe));
        return Evaluate(built, head.Value);
    }

    /// <summary>
    /// Pure comparison core: a notice is warranted only when the latest commit is
    /// strictly newer than the build. <see cref="Result.DaysBehind"/> floors the
    /// gap to whole days (so a few hours behind reports 0, "behind today").
    /// </summary>
    public static Result? Evaluate(DateTimeOffset exeBuilt, DateTimeOffset headCommitted)
    {
        if (headCommitted <= exeBuilt) return null;
        var days = (int)Math.Floor((headCommitted - exeBuilt).TotalDays);
        return new Result(days, exeBuilt, headCommitted);
    }

    private static DateTimeOffset? LatestCommitTime(string root)
    {
        try
        {
            var psi = new ProcessStartInfo("git")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = root
            };
            psi.ArgumentList.Add("log");
            psi.ArgumentList.Add("-1");
            psi.ArgumentList.Add("--format=%cI"); // strict ISO-8601 with offset

            using var p = Process.Start(psi);
            if (p is null) return null;

            var stdout = p.StandardOutput.ReadToEnd();
            _ = p.StandardError.ReadToEnd(); // drain so a chatty git can't deadlock the pipe
            if (!p.WaitForExit(3000))
            {
                try { p.Kill(entireProcessTree: true); } catch { /* best-effort */ }
                return null;
            }
            if (p.ExitCode != 0) return null;

            // %cI is strict ISO-8601 with offset; parse it invariantly and keep the
            // offset (RoundtripKind) so a non-Gregorian / non-invariant system
            // culture can't misread the year or shift the instant.
            return DateTimeOffset.TryParse(
                stdout.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var when)
                ? when
                : null;
        }
        catch
        {
            // git missing / not on PATH / not a repo — degrade to no notice.
            return null;
        }
    }
}
