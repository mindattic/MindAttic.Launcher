using System.Runtime.InteropServices;

namespace MindAttic.Launcher.Services;

/// <summary>
/// Thin, defensive wrapper around <c>VirtualDesktopAccessor.dll</c> (Ciantic/VirtualDesktopAccessor, MIT,
/// <see href="https://github.com/Ciantic/VirtualDesktopAccessor"/>, vendored under <c>Native\</c>): the one piece
/// Windows has no stable public API for — enumerating virtual desktops and moving an arbitrary window to one by
/// index. Desktop numbers are 0-based (desktop 0 is "Desktop 1" in Task View); the DLL itself returns -1 on error.
/// <para>Chosen over hand-rolled COM interop against the undocumented <c>IVirtualDesktopManagerInternal</c> family
/// (whose GUIDs Microsoft has broken on more than one Windows 11 feature update, most recently 23H2 build 3085):
/// this library tracks that churn upstream (MIT, actively maintained, explicitly tested against Windows 11 25H2
/// build 26200 — this machine's own build) behind five flat <c>extern "C"</c> exports, so DesktopPinWatcher never
/// needs its own per-build GUID table. Every call here is wrapped so a future Windows update that breaks the DLL's
/// own interop (missing export, wrong calling convention, a build it doesn't recognize) degrades to "don't move the
/// window" rather than a crash or a wrong move — see <see cref="DesktopPinWatcher"/>'s safety posture.</para>
/// </summary>
internal static class VirtualDesktops
{
    private const string DllName = "VirtualDesktopAccessor.dll";

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int GetDesktopCount();

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int GetWindowDesktopNumber(IntPtr hwnd);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int MoveWindowToDesktopNumber(IntPtr hwnd, int desktopNumber);

    /// <summary>The number of virtual desktops that currently exist, or null when the DLL can't answer (missing,
    /// wrong architecture, an export it doesn't have, or any other interop failure).</summary>
    public static int? TryGetDesktopCount()
    {
        try
        {
            var n = GetDesktopCount();
            return n >= 1 ? n : null;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or SEHException)
        {
            return null;
        }
    }

    /// <summary>The 0-based desktop number <paramref name="hwnd"/> is currently on, or null when it can't be
    /// determined (the DLL failed, or the window has no desktop association, e.g. it already closed).</summary>
    public static int? TryGetWindowDesktopNumber(IntPtr hwnd)
    {
        try
        {
            var n = GetWindowDesktopNumber(hwnd);
            return n >= 0 ? n : null;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or SEHException)
        {
            return null;
        }
    }

    /// <summary>Moves <paramref name="hwnd"/> to 0-based desktop <paramref name="desktopNumber"/>. Returns whether
    /// the DLL reported success; never throws.</summary>
    public static bool TryMoveWindowToDesktop(IntPtr hwnd, int desktopNumber)
    {
        try
        {
            return MoveWindowToDesktopNumber(hwnd, desktopNumber) >= 0;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or SEHException)
        {
            return false;
        }
    }
}
