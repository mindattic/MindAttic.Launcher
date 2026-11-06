namespace MindAttic.Launcher.Services;

/// <summary>
/// The pure decision rules behind <see cref="DesktopPinWatcher"/>, split out from the Win32 plumbing so they're
/// unit-testable: whether a window's owning process counts as "MindAttic" (M.LNCH-T111's hard scope — terminals,
/// Unity/Unreal apps, any .exe under the workspace root — and nothing else, ever), and whether a window that does
/// should actually be moved right now.
/// </summary>
internal static class DesktopPinPolicy
{
    /// <summary>
    /// True only when <paramref name="imagePath"/> resolves to a file strictly under <paramref name="root"/> (a
    /// subfolder or file of it — never an unrelated path that merely shares <paramref name="root"/> as a string
    /// prefix, e.g. <c>D:\Projects\MindAtticX</c> must not match <c>D:\Projects\MindAttic</c>). This is the entire
    /// ecosystem filter: Ryan was explicit that this must never grab a window outside the MindAttic tree (a
    /// browser, Discord, a Steam game that isn't part of this repo), so an unresolvable or blank path is "no" —
    /// never a guess.
    /// </summary>
    public static bool IsUnderMindAtticRoot(string? imagePath, string? root)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || string.IsNullOrWhiteSpace(root)) return false;

        string fullPath, fullRoot;
        try
        {
            fullPath = Path.GetFullPath(imagePath);
            fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        if (fullRoot.Length == 0 || fullPath.Length <= fullRoot.Length) return false;
        if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)) return false;

        var boundary = fullPath[fullRoot.Length];
        return boundary is '\\' or '/';
    }

    /// <summary>
    /// Whether a MindAttic window should be moved to <paramref name="targetDesktopNumber"/> (0 = Desktop 1) right
    /// now: the setting must be on, more than one virtual desktop must currently exist, and the window's current
    /// desktop must be known and not already the target. Any unknown input (a desktop count or window desktop we
    /// couldn't determine) means "do nothing" — never act on a guess.
    /// </summary>
    public static bool ShouldPin(bool settingOn, int? desktopCount, int? currentDesktopNumber, int targetDesktopNumber = 0)
    {
        if (!settingOn) return false;
        if (desktopCount is not { } count || count <= 1) return false;
        if (currentDesktopNumber is not { } current) return false;
        return current != targetDesktopNumber;
    }
}
