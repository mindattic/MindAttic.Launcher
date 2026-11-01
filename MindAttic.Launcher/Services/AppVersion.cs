using System.Reflection;

namespace MindAttic.Launcher.Services;

/// <summary>
/// Whole-number build tag (M.LNCH-T106): the csproj's major-only &lt;Version&gt;
/// (1.0.0 -&gt; "v1"), so a glance at the corner tag says which build is running.
/// </summary>
public static class AppVersion
{
    public static string Tag { get; } = BuildTag();

    private static string BuildTag()
    {
        var asm = typeof(AppVersion).Assembly;
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                   ?? asm.GetName().Version?.ToString();
        if (string.IsNullOrEmpty(info)) return "v?";

        var major = info.Split('.', '-')[0];
        return $"v{major}";
    }
}
