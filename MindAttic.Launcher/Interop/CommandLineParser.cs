using System.ComponentModel;
using System.Runtime.InteropServices;

namespace MindAttic.Launcher.Interop;

/// <summary>
/// Splits a Windows command line into argv exactly as <c>CreateProcessW</c>
/// would, via <c>CommandLineToArgvW</c>, so quoted args in a provider's
/// <c>RunCommand</c> survive intact.
/// </summary>
public static partial class CommandLineParser
{
    public static string[] Split(string commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine)) return [];

        var argv = CommandLineToArgvW(commandLine, out var argc);
        if (argv == IntPtr.Zero) throw new Win32Exception();

        try
        {
            var result = new string[argc];
            for (var i = 0; i < argc; i++)
            {
                var arg = Marshal.ReadIntPtr(argv, i * IntPtr.Size);
                result[i] = Marshal.PtrToStringUni(arg) ?? "";
            }
            return result;
        }
        finally
        {
            LocalFree(argv);
        }
    }

    [LibraryImport("shell32.dll", EntryPoint = "CommandLineToArgvW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial IntPtr CommandLineToArgvW(string lpCmdLine, out int pNumArgs);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr LocalFree(IntPtr hMem);
}
