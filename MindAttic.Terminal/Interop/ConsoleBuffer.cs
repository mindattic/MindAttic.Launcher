using System.Runtime.InteropServices;
using System.Text;

namespace MindAttic.Terminal.Interop;

/// <summary>
/// Peeks the bottom rows of the current console screen buffer so the agent
/// host can detect Claude/Codex "esc to interrupt" prompts and decide whether
/// to pin the tab title with the idle marker.
/// </summary>
public static partial class ConsoleBuffer
{
    private const int STD_OUTPUT_HANDLE = -11;

    public static string ReadBottomRows(int rowCount)
    {
        if (rowCount <= 0) return string.Empty;

        var handle = GetStdHandle(STD_OUTPUT_HANDLE);
        if (!GetConsoleScreenBufferInfo(handle, out var info)) return string.Empty;

        var width = info.srWindow.Right - info.srWindow.Left + 1;
        var bottom = info.srWindow.Bottom;
        var top = Math.Max((int)info.srWindow.Top, bottom - rowCount + 1);
        if (width <= 0 || top > bottom) return string.Empty;

        var sb = new StringBuilder();
        for (var y = top; y <= bottom; y++)
        {
            var row = new StringBuilder(width);
            row.Length = width;
            var coord = new COORD { X = info.srWindow.Left, Y = (short)y };
            if (ReadConsoleOutputCharacterW(handle, row, (uint)width, coord, out var read))
            {
                sb.Append(row.ToString(0, (int)read));
                sb.Append('\n');
            }
        }
        return sb.ToString();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct COORD { public short X; public short Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct SMALL_RECT { public short Left; public short Top; public short Right; public short Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct CONSOLE_SCREEN_BUFFER_INFO
    {
        public COORD dwSize;
        public COORD dwCursorPosition;
        public short wAttributes;
        public SMALL_RECT srWindow;
        public COORD dwMaximumWindowSize;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr GetStdHandle(int nStdHandle);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetConsoleScreenBufferInfo(IntPtr handle, out CONSOLE_SCREEN_BUFFER_INFO info);

    // LibraryImport can't marshal StringBuilder directly, so this one stays DllImport.
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "ReadConsoleOutputCharacterW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadConsoleOutputCharacterW(IntPtr handle, [Out] StringBuilder lpCharacter, uint length, COORD readCoord, out uint numRead);
}
