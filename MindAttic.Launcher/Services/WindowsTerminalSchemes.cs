namespace MindAttic.Launcher.Services;

/// <summary>
/// Writes a <c>MindAttic-&lt;Name&gt;</c> color scheme into the Windows Terminal
/// settings.json. Every existing MindAttic scheme shares one ANSI palette and
/// differs only by <c>background</c>, so a new one is a fixed block with the
/// chosen background substituted in.
///
/// The insert is a targeted text edit (locate the <c>schemes</c> array, splice a
/// scheme object after its opening bracket) rather than a parse-and-reserialize:
/// the WT file is large and user-owned, and round-tripping it through a JSON
/// serializer would reflow the whole document. The pure splice in
/// <see cref="TryInsertScheme"/> touches only the bytes it adds.
/// </summary>
public sealed class WindowsTerminalSchemes
{
    /// <summary>
    /// Splices a scheme named <paramref name="schemeName"/> into the
    /// <c>schemes</c> array of <paramref name="contents"/>. Idempotent: if a
    /// scheme with that name already exists the contents are returned unchanged.
    /// Returns false (with <paramref name="result"/> = original) when no
    /// <c>schemes</c> array can be located.
    /// </summary>
    public static bool TryInsertScheme(string contents, string schemeName, string backgroundHex, out string result)
    {
        result = contents;
        if (string.IsNullOrEmpty(contents)) return false;

        var keyIdx = contents.IndexOf("\"schemes\"", StringComparison.Ordinal);
        if (keyIdx < 0) return false;

        var openIdx = contents.IndexOf('[', keyIdx);
        if (openIdx < 0) return false;

        // The array's ']' — scheme objects hold no nested arrays, so the first
        // ']' after '[' closes the schemes array.
        var closeIdx = contents.IndexOf(']', openIdx);
        if (closeIdx < 0) return false;

        // Already present — but only count a match *inside the schemes array*.
        // A profile or theme named the same (WT files have a "profiles" list with
        // its own "name" keys) must not make us think the scheme exists and skip
        // writing it while still reporting success. Match the exact spacing WT and
        // this writer emit.
        var existingIdx = contents.IndexOf($"\"name\": \"{schemeName}\"", openIdx, StringComparison.Ordinal);
        if (existingIdx >= 0 && existingIdx < closeIdx)
            return true;

        // Is the array empty? (next non-whitespace after '[' is ']')
        var cursor = openIdx + 1;
        while (cursor < contents.Length && char.IsWhiteSpace(contents[cursor])) cursor++;
        var isEmpty = cursor < contents.Length && contents[cursor] == ']';

        var block = BuildSchemeBlock(schemeName, backgroundHex);
        var insertion = isEmpty
            ? $"\n{block}\n    "          // drop the array's ']' onto its own indented line
            : $"\n{block},";              // existing first entry follows after the comma

        // For the empty case, splice in front of the ']' so we don't leave the
        // original empty-array whitespace between our block and the bracket.
        var spliceAt = isEmpty ? cursor : openIdx + 1;
        result = contents[..spliceAt] + insertion + (isEmpty ? contents[cursor..] : contents[(openIdx + 1)..]);
        return true;
    }

    /// <summary>
    /// Ensures the WT settings file at <paramref name="wtSettingsPath"/> contains
    /// a scheme named <paramref name="schemeName"/> with the given background.
    /// Returns false (and writes nothing) when the path is missing/blank, the
    /// file can't be read/written, or it has no <c>schemes</c> array — the caller
    /// adds the project anyway and notes that the scheme wasn't written.
    /// </summary>
    public bool EnsureScheme(string? wtSettingsPath, string schemeName, string backgroundHex)
    {
        if (string.IsNullOrWhiteSpace(wtSettingsPath) || !File.Exists(wtSettingsPath))
            return false;

        try
        {
            var contents = File.ReadAllText(wtSettingsPath);
            if (!TryInsertScheme(contents, schemeName, backgroundHex, out var updated))
                return false;
            if (!ReferenceEquals(updated, contents) && updated != contents)
                File.WriteAllText(wtSettingsPath, updated);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    // Built from explicit lines (not a raw string literal) so the emitted
    // 8-space/12-space indentation is unambiguous and matches the existing
    // scheme objects in the WT file exactly.
    private static string BuildSchemeBlock(string schemeName, string backgroundHex)
    {
        string[] lines =
        [
            "        {",
            $"            \"name\": \"{schemeName}\",",
            $"            \"background\": \"{backgroundHex}\",",
            "            \"foreground\": \"#CCCCCC\",",
            "            \"cursorColor\": \"#FFFFFF\",",
            "            \"selectionBackground\": \"#FFFFFF\",",
            "            \"black\": \"#0C0C0C\", \"red\": \"#C50F1F\", \"green\": \"#13A10E\", \"yellow\": \"#C19C00\",",
            "            \"blue\": \"#0037DA\", \"purple\": \"#881798\", \"cyan\": \"#3A96DD\", \"white\": \"#CCCCCC\",",
            "            \"brightBlack\": \"#767676\", \"brightRed\": \"#E74856\", \"brightGreen\": \"#16C60C\", \"brightYellow\": \"#F9F1A5\",",
            "            \"brightBlue\": \"#3B78FF\", \"brightPurple\": \"#B4009E\", \"brightCyan\": \"#61D6D6\", \"brightWhite\": \"#F2F2F2\"",
            "        }",
        ];
        return string.Join('\n', lines);
    }
}
