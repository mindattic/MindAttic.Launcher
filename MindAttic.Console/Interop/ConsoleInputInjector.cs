using System.Runtime.InteropServices;

namespace MindAttic.Console.Interop;

/// <summary>
/// Injects synthetic keystrokes into the current console's input buffer via
/// WriteConsoleInputW. Child processes (Claude, Codex) that inherit stdio read
/// the injected events as if the user had typed them, which is how the
/// "Remote Control" menu types `/remote-control` into every open agent tab
/// without having to focus each wt tab.
/// </summary>
public static partial class ConsoleInputInjector
{
    private const int STD_INPUT_HANDLE = -10;
    private const ushort KEY_EVENT = 0x0001;
    private const ushort VK_RETURN = 0x0D;

    public static void InjectText(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        var handle = GetStdHandle(STD_INPUT_HANDLE);
        if (handle == IntPtr.Zero || handle == new IntPtr(-1)) return;

        var records = new List<INPUT_RECORD>(text.Length * 2);
        foreach (var ch in text)
        {
            if (ch == '\r') continue; // collapse CRLF; the '\n' branch emits VK_RETURN
            if (ch == '\n')
            {
                records.Add(KeyRecord(bKeyDown: true,  vkey: VK_RETURN, ch: '\r'));
                records.Add(KeyRecord(bKeyDown: false, vkey: VK_RETURN, ch: '\r'));
                continue;
            }
            records.Add(KeyRecord(bKeyDown: true,  vkey: 0, ch: ch));
            records.Add(KeyRecord(bKeyDown: false, vkey: 0, ch: ch));
        }

        var buffer = records.ToArray();
        // Surface failures so the pipe server / broadcaster don't report
        // "Sent /remote-control to N tabs" when the input handle is closed or
        // the call failed for some other reason.
        if (!WriteConsoleInputW(handle, buffer, (uint)buffer.Length, out _))
        {
            var err = Marshal.GetLastWin32Error();
            throw new System.ComponentModel.Win32Exception(err, "WriteConsoleInputW failed");
        }
    }

    private static INPUT_RECORD KeyRecord(bool bKeyDown, ushort vkey, char ch) => new()
    {
        EventType = KEY_EVENT,
        KeyEvent = new KEY_EVENT_RECORD
        {
            bKeyDown = bKeyDown ? 1 : 0,
            wRepeatCount = 1,
            wVirtualKeyCode = vkey,
            wVirtualScanCode = 0,
            UnicodeChar = ch,
            dwControlKeyState = 0
        }
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct KEY_EVENT_RECORD
    {
        public int bKeyDown;
        public ushort wRepeatCount;
        public ushort wVirtualKeyCode;
        public ushort wVirtualScanCode;
        public char UnicodeChar;
        public uint dwControlKeyState;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUT_RECORD
    {
        [FieldOffset(0)] public ushort EventType;
        [FieldOffset(4)] public KEY_EVENT_RECORD KeyEvent;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr GetStdHandle(int nStdHandle);

    // LibraryImport can't blittably marshal INPUT_RECORD[] (the explicit-layout
    // union trips SYSLIB1051), so this one stays DllImport — same pattern as
    // ReadConsoleOutputCharacterW in ConsoleBuffer.
    [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "WriteConsoleInputW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WriteConsoleInputW(
        IntPtr hConsoleInput,
        [In] INPUT_RECORD[] lpBuffer,
        uint nLength,
        out uint lpNumberOfEventsWritten);
}
