using System.Globalization;

namespace MindAttic.Launcher.Services;

/// <summary>A named tab color the discovery flow offers when adding a project.</summary>
public sealed record PaletteColor(string Name, string Hex);

/// <summary>
/// Curated tab-color palette plus the pure helpers that turn a chosen color into
/// a Windows Terminal scheme. Every MindAttic WT scheme shares one ANSI palette
/// and differs only by <c>background</c>, so all we need from a color is a name,
/// the tab hex, and a dark background tint derived from it.
/// </summary>
public static class ColorPalette
{
    private const string NamePrefix = "MindAttic.";

    /// <summary>
    /// Visually distinct named colors offered when adding a discovered project.
    /// Spread across the wheel so adjacent picks read differently on a tab strip.
    /// </summary>
    public static readonly IReadOnlyList<PaletteColor> Colors = new[]
    {
        new PaletteColor("Emerald",   "#2E7D32"),
        new PaletteColor("Teal",      "#00897B"),
        new PaletteColor("Mint",      "#26A69A"),
        new PaletteColor("Cyan",      "#00BCD4"),
        new PaletteColor("Azure",     "#3C82FF"),
        new PaletteColor("Indigo",    "#3F51B5"),
        new PaletteColor("Violet",    "#7C4DFF"),
        new PaletteColor("Purple",    "#9C27B0"),
        new PaletteColor("Magenta",   "#D81B60"),
        new PaletteColor("Crimson",   "#C0392B"),
        new PaletteColor("Coral",     "#FF6F61"),
        new PaletteColor("Orange",    "#FB8C00"),
        new PaletteColor("Amber",     "#F5A623"),
        new PaletteColor("Gold",      "#B8860B"),
        new PaletteColor("Lime",      "#7CB342"),
        new PaletteColor("Slate",     "#546E7A"),
    };

    /// <summary>
    /// The WT scheme name for a project: <c>MindAttic-&lt;short name&gt;</c>, where
    /// the shared "MindAttic." prefix is stripped (matching the existing
    /// MindAttic-Vault / MindAttic-ChiMesh schemes and <see cref="Models.Project.TabTitle"/>).
    /// </summary>
    public static string SchemeName(string projectName)
    {
        var shortName = projectName.StartsWith(NamePrefix, StringComparison.OrdinalIgnoreCase)
            ? projectName[NamePrefix.Length..]
            : projectName;
        return $"MindAttic-{shortName}";
    }

    /// <summary>
    /// Derives the dark scheme background from a tab color by scaling each RGB
    /// channel down to ~16% brightness — the same near-black tint the existing
    /// schemes use. Works for any valid <c>#RRGGBB</c>, including custom hex.
    /// </summary>
    public static string DarkBackground(string hex)
    {
        if (!TryParseRgb(hex, out var r, out var g, out var b))
            return "#0C0C0C"; // fall back to the shared "black" if the input is junk

        const double scale = 0.16;
        var dr = (int)Math.Round(r * scale);
        var dg = (int)Math.Round(g * scale);
        var db = (int)Math.Round(b * scale);
        return $"#{dr:X2}{dg:X2}{db:X2}";
    }

    /// <summary>
    /// Validates and normalizes user-typed hex (<c>2e7d32</c>, <c>#2E7D32</c>) to
    /// canonical <c>#RRGGBB</c> upper-case form. Returns false for anything that
    /// isn't six hex digits.
    /// </summary>
    public static bool TryNormalizeHex(string? input, out string hex)
    {
        hex = "";
        if (string.IsNullOrWhiteSpace(input)) return false;
        if (!TryParseRgb(input, out var r, out var g, out var b)) return false;
        hex = $"#{r:X2}{g:X2}{b:X2}";
        return true;
    }

    private static bool TryParseRgb(string? input, out int r, out int g, out int b)
    {
        r = g = b = 0;
        if (string.IsNullOrWhiteSpace(input)) return false;
        var s = input.Trim();
        if (s.StartsWith('#')) s = s[1..];
        if (s.Length != 6) return false;

        return TryHex(s[..2], out r) && TryHex(s.Substring(2, 2), out g) && TryHex(s.Substring(4, 2), out b);

        static bool TryHex(string pair, out int value) =>
            int.TryParse(pair, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
    }
}
