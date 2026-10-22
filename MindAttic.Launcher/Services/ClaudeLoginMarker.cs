using System.IO.Pipes;
using System.Text;

namespace MindAttic.Launcher.Services;

/// <summary>
/// Marks a Claude login as "has a CLI right now" (M.LNCH-T53), using the same named-pipe idiom
/// <see cref="HostInputPipeServer"/> and <see cref="HostControlPipeServer"/> use to claim a tab-scoped name:
/// a single-instance pipe (<c>maxNumberOfServerInstances: 1</c>) that simply fails to be created when another
/// one for the same login already exists. Nothing is ever sent on it — its mere existence is the marker, so
/// there's no connection to wait for and no payload to parse. A host creates one for the login it starts on
/// (<see cref="MindAttic.Launcher.Commands.HostAgentCommand"/>) and disposes it on exit; the account picker
/// (<see cref="ClaudeAccountCatalog"/>) calls <see cref="IsBusy"/> to ask whether a login already has one, so
/// the user doesn't accidentally pick a login another tab is already running under.
/// </summary>
public sealed class ClaudeLoginMarker : IDisposable
{
    public const string PipeNamePrefix = "mindattic-login-";

    private readonly NamedPipeServerStream? server;

    /// <summary>Claims <paramref name="configDir"/>'s marker for the lifetime of this instance. Failing to
    /// claim it (another tab already holds it) is never fatal — the marker is purely advisory.</summary>
    public ClaudeLoginMarker(string configDir)
    {
        try { server = new NamedPipeServerStream(PipeName(configDir), PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous); }
        catch (IOException) { server = null; }
    }

    public static string PipeName(string configDir) => PipeNamePrefix + Slug(configDir);

    /// <summary>True when another process already holds <paramref name="configDir"/>'s marker pipe, i.e. a
    /// CLI is already running under that login.</summary>
    public static bool IsBusy(string configDir)
    {
        try
        {
            using var probe = new NamedPipeServerStream(PipeName(configDir), PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
    }

    /// <summary>A pipe-name-safe key for <paramref name="configDir"/>: "global" for "" (the global login),
    /// else the lowercased configDir with anything but letters/digits folded to '-'.</summary>
    private static string Slug(string configDir)
    {
        var key = string.IsNullOrWhiteSpace(configDir) ? "global" : configDir.Trim().ToLowerInvariant();
        var sb = new StringBuilder(key.Length);
        foreach (var c in key) sb.Append(char.IsLetterOrDigit(c) ? c : '-');
        return sb.ToString();
    }

    public void Dispose() => server?.Dispose();
}
