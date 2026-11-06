using System.Diagnostics;
using System.Runtime.InteropServices;
using MindAttic.Vault.Settings;

namespace MindAttic.Launcher.Services;

/// <summary>
/// M.LNCH-T111: keeps MindAttic windows off whatever virtual desktop (2+) the user is gaming on, and off Desktop 1
/// no faster than they'd otherwise land there — never the reverse. Hosted by the Launcher's own interactive menu
/// process (the one singleton "program" instance per <c>mindattic-agent-standard</c>'s own distinction between it
/// and a per-tab <c>host</c>/<c>commit</c>/… invocation), started once in <see cref="Commands.MainMenuCommand"/>.
///
/// <para><b>Detection:</b> <c>SetWinEventHook</c> on <c>EVENT_OBJECT_SHOW</c> and <c>EVENT_SYSTEM_FOREGROUND</c>,
/// each with its own exact-range hook (never a <c>min..max</c> span across the two, which would also deliver every
/// event code in between) — the two events Microsoft documents for "a top-level window just became visible/active".
/// <c>WINEVENT_OUTOFCONTEXT</c> (no DLL injection) and <c>WINEVENT_SKIPOWNPROCESS</c> (this process's own windows,
/// e.g. the console host, are never candidates). The callback keeps only <c>idObject == OBJID_WINDOW</c> and
/// <c>idChild == CHILDID_SELF</c> — an actual top-level window object, not a child control, caret or cursor event —
/// and only a window that is its own <c>GetAncestor(..., GA_ROOT)</c> and currently visible.</para>
///
/// <para><b>Scope (hard, see <see cref="DesktopPinPolicy.IsUnderMindAtticRoot"/>):</b> the event fires for every
/// top-level window shown anywhere on the system — a browser tab, Discord, a Steam game with nothing to do with
/// this repo. Ryan was explicit that this must never be a system-wide hook that disturbs anything outside the
/// MindAttic ecosystem, so the one thing this class trusts is the owning process's own image path, resolved by PID
/// (never a window title or class name, which can be spoofed or coincidental) and checked strictly against
/// <see cref="ExePath.WorkspaceRoot"/> — resolved at runtime from the running Launcher's own repo location, not a
/// hardcoded drive letter, with <see cref="ExePath.WorkspaceRoot"/>'s own documented fallback for the rare case it
/// can't be found. A path that can't be resolved (the process already exited, or access is denied — an elevated or
/// protected process) is treated as "not MindAttic": this class only ever acts on a window it could positively
/// identify, never on a guess.</para>
///
/// <para><b>The move itself</b> goes through <see cref="VirtualDesktops"/> (Ciantic/VirtualDesktopAccessor), only
/// when <see cref="MindAtticGlobalSettings.DefaultToDesktop1"/> is on, more than one virtual desktop currently
/// exists, and the window isn't already on Desktop 1 (0-based desktop 0) — see
/// <see cref="DesktopPinPolicy.ShouldPin"/>. A single desktop existing is "nothing to do", exactly as specified.
/// </para>
///
/// <para><b>Failure posture:</b> every step from the raw event onward is wrapped so one bad window, a denied
/// process handle, or a future Windows update that breaks the vendored DLL's own interop degrades to "this window
/// wasn't moved" — logged, never thrown, and never a crash of the Launcher menu hosting it.</para>
/// </summary>
public sealed class DesktopPinWatcher : IDisposable
{
    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const uint EVENT_OBJECT_SHOW = 0x8002;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    private const uint WINEVENT_SKIPOWNPROCESS = 0x0002;
    private const int OBJID_WINDOW = 0;
    private const int CHILDID_SELF = 0;
    private const uint GA_ROOT = 2;
    private const uint WM_QUIT = 0x0012;

    private delegate void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
        WinEventProc lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    [DllImport("user32.dll")]
    private static extern bool GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

    private readonly string mindAtticRoot;

    // Kept as fields, not locals: native code holds a raw pointer into these delegates for as long as the hooks
    // are installed, and a delegate with nothing managed referencing it is fair game for the GC to collect out
    // from under that pointer.
    private WinEventProc? showCallback;
    private WinEventProc? foregroundCallback;

    private IntPtr showHook;
    private IntPtr foregroundHook;
    private Thread? thread;
    private volatile uint nativeThreadId;

    // A window already confirmed "on Desktop 1" or just moved there isn't re-decided on every later
    // EVENT_OBJECT_SHOW its own child controls can raise for the same top-level hwnd. Unbounded, but
    // bounded in practice by how many MindAttic top-level windows exist at once (a handful at most); an
    // hwnd is never reused for another window while still in this process's lifetime risk is the same
    // one every "seen window handles" cache in Win32 code accepts.
    private readonly HashSet<IntPtr> settled = [];

    public DesktopPinWatcher(string? mindAtticRoot = null)
    {
        this.mindAtticRoot = mindAtticRoot ?? ExePath.WorkspaceRoot;
    }

    /// <summary>Starts the background hook thread. Idempotent — a second call is a no-op.</summary>
    public void Start()
    {
        if (thread is not null) return;
        thread = new Thread(RunMessageLoop) { IsBackground = true, Name = "MindAttic.DesktopPinWatcher" };
        thread.Start();
    }

    private void RunMessageLoop()
    {
        nativeThreadId = GetCurrentThreadId();

        // SetWinEventHook(min, max, ...) delivers every event code in the range, not just the named ones — two
        // separate hooks, each min == max, is the only way to ask for exactly these two events.
        showCallback = OnWinEvent;
        foregroundCallback = OnWinEvent;

        try
        {
            showHook = SetWinEventHook(EVENT_OBJECT_SHOW, EVENT_OBJECT_SHOW, IntPtr.Zero, showCallback,
                0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
            foregroundHook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, foregroundCallback,
                0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);

            if (showHook == IntPtr.Zero && foregroundHook == IntPtr.Zero)
            {
                Serilog.Log.Warning("DesktopPinWatcher: SetWinEventHook failed for both events; the watcher is disabled for this run.");
                return;
            }

            // A message loop is required for WINEVENT_OUTOFCONTEXT callbacks to be delivered to this thread at
            // all — nothing else on this thread needs it; it exists solely to pump the hook.
            while (GetMessage(out var msg, IntPtr.Zero, 0, 0))
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "DesktopPinWatcher: stopped after an unexpected error");
        }
        finally
        {
            if (showHook != IntPtr.Zero) { UnhookWinEvent(showHook); showHook = IntPtr.Zero; }
            if (foregroundHook != IntPtr.Zero) { UnhookWinEvent(foregroundHook); foregroundHook = IntPtr.Zero; }
        }
    }

    private void OnWinEvent(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        try
        {
            if (idObject != OBJID_WINDOW || idChild != CHILDID_SELF) return; // a real top-level window object only
            if (hwnd == IntPtr.Zero || settled.Contains(hwnd)) return;
            if (!IsWindowVisible(hwnd)) return;
            if (GetAncestor(hwnd, GA_ROOT) != hwnd) return; // top-level only, not a child control or owned popup

            var imagePath = TryGetProcessImagePath(hwnd);
            if (!DesktopPinPolicy.IsUnderMindAtticRoot(imagePath, mindAtticRoot)) return;

            MindAtticGlobalSettings settings;
            try { settings = MindAtticGlobalSettings.Store().Load(); }
            catch (Exception ex)
            {
                // Can't confirm the setting is actually on: do nothing rather than guess.
                Serilog.Log.Warning(ex, "DesktopPinWatcher: couldn't read Vault's Global settings; leaving {Window} alone", imagePath);
                return;
            }

            var count = VirtualDesktops.TryGetDesktopCount();
            var current = VirtualDesktops.TryGetWindowDesktopNumber(hwnd);

            if (!DesktopPinPolicy.ShouldPin(settings.DefaultToDesktop1, count, current))
            {
                if (current == 0) settled.Add(hwnd); // already home; stop re-checking this window
                return;
            }

            if (VirtualDesktops.TryMoveWindowToDesktop(hwnd, 0))
            {
                settled.Add(hwnd);
                Serilog.Log.Information("DesktopPinWatcher: moved {Window} to Desktop 1 (was desktop {From})", imagePath, current);
            }
        }
        catch (Exception ex)
        {
            // One bad event must never take the hook thread — or the Launcher menu hosting it — down.
            try { Serilog.Log.Warning(ex, "DesktopPinWatcher: skipped a window after an unexpected error"); } catch { /* logging itself must never throw further */ }
        }
    }

    private static string? TryGetProcessImagePath(IntPtr hwnd)
    {
        try
        {
            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == 0) return null;
            using var process = Process.GetProcessById((int)pid);
            return process.MainModule?.FileName;
        }
        catch
        {
            // Exited already, access denied (an elevated, system or otherwise protected process), or a 32/64-bit
            // mismatch reading MainModule: we can't positively identify this window, so — conservatively — leave
            // it alone rather than guess it might be MindAttic's.
            return null;
        }
    }

    /// <summary>Stops the hook thread. Best-effort and safe to call more than once.</summary>
    public void Dispose()
    {
        if (thread is null) return;
        try
        {
            if (nativeThreadId != 0) PostThreadMessage(nativeThreadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            thread.Join(TimeSpan.FromSeconds(2));
        }
        catch { /* best-effort shutdown; the hooks are also torn down by process exit regardless */ }
        thread = null;
    }
}
