using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MindAttic.Launcher.Services;

/// <summary>
/// Each Claude login's own Windows Terminal profile (M.LNCH-T56), so a tab shows whose it is as an icon as well as its
/// "JR:" title badge: a Terminal fragment in <see cref="FragmentsDir"/> (by default
/// <c>%LOCALAPPDATA%\Microsoft\Windows Terminal\Fragments\MindAttic</c>, one <c>&lt;login&gt;.json</c> per login) whose
/// profile's icon is a generated image of the login's initials on its tab color. A login's tabs open with it
/// (<c>wt new-tab -p {guid}</c>). The profile is keyed by the login's email, so a new alias or initials rewrites the same
/// profile (new name, new icon) rather than adding another; the fragment and icon are only written when they differ from
/// what's there. The fragments folder is injectable so tests never touch the real one, nor Terminal's own settings.json.
/// </summary>
public sealed class LoginTerminalProfiles(string? fragmentsDir = null)
{
    /// <summary>Windows Terminal reads every <c>*.json</c> in a folder under <c>Fragments</c> as one app's fragments.</summary>
    public static string DefaultFragmentsDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows Terminal", "Fragments", "MindAttic");

    public string FragmentsDir { get; } = fragmentsDir ?? DefaultFragmentsDir;

    /// <summary>The icon's size in pixels; Terminal scales it down for the tab and the profile menu.</summary>
    public const int IconSize = 64;

    /// <summary>The profile's guid, as <c>wt -p</c> takes it: fixed for a login (its email, else its window name) so the
    /// tab can name it before the fragment is written, and across renames.</summary>
    public static string ProfileGuid(LoginLook look)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes("MindAttic.Launcher/login/" + Key(look)));
        hash[6] = (byte)((hash[6] & 0x0F) | 0x30); // RFC 4122 version 3 (name-based, MD5)
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        return "{" + new Guid(hash, bigEndian: true).ToString() + "}";
    }

    /// <summary>The profile's name in Terminal's profile menu.</summary>
    public static string ProfileName(LoginLook look) => $"Claude - {look.Alias}";

    /// <summary>The fragment file of <paramref name="look"/>'s login.</summary>
    public string FragmentPath(LoginLook look) => Path.Combine(FragmentsDir, FileKey(look) + ".json");

    /// <summary>The icon of <paramref name="look"/>'s login as it looks now: named for its initials and color, since Terminal
    /// caches an icon by its path and wouldn't show a redrawn one under the same name.</summary>
    public string IconPath(LoginLook look) =>
        Path.Combine(FragmentsDir, "icons", FileKey(look), $"{FileKey(look.Initials)}-{look.TabColor.TrimStart('#')}.png");

    /// <summary>Writes (or rewrites) <paramref name="look"/>'s login's fragment and icon when they aren't already as it looks
    /// now, removing its outdated icons; returns the profile's guid for <c>wt -p</c>, or null when it couldn't be written
    /// (the tab then opens with Terminal's default profile, still badged and colored).</summary>
    public string? Ensure(LoginLook look)
    {
        try
        {
            var icon = IconPath(look);
            var iconDir = Path.GetDirectoryName(icon)!;
            Directory.CreateDirectory(iconDir);
            if (!File.Exists(icon))
            {
                var temp = icon + ".tmp";
                File.WriteAllBytes(temp, RenderIcon(look.Initials, look.TabColor));
                File.Move(temp, icon, overwrite: true);
            }
            foreach (var old in Directory.EnumerateFiles(iconDir, "*.png"))
                if (!string.Equals(old, icon, StringComparison.OrdinalIgnoreCase))
                    TryDelete(old);

            var path = FragmentPath(look);
            var json = FragmentJson(look, icon);
            if (!File.Exists(path) || File.ReadAllText(path) != json)
            {
                var temp = path + ".tmp";
                File.WriteAllText(temp, json, new UTF8Encoding(false));
                File.Move(temp, path, overwrite: true);
            }
            return ProfileGuid(look);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ExternalException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>The fragment for <paramref name="look"/>'s login: one profile, named and iconed as the login and colored as
    /// its tabs. Its command line is only Terminal's fallback; the Launcher's tabs pass their own after <c>--</c>.</summary>
    public static string FragmentJson(LoginLook look, string iconPath)
    {
        var profile = new JsonObject
        {
            ["guid"] = ProfileGuid(look),
            ["name"] = ProfileName(look),
            ["icon"] = iconPath,
            ["tabColor"] = look.TabColor,
            ["commandline"] = "%SystemRoot%\\System32\\cmd.exe",
        };
        var fragment = new JsonObject { ["profiles"] = new JsonArray(profile) };
        return fragment.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";
    }

    /// <summary>A PNG of <paramref name="initials"/> in bold on a rounded square of <paramref name="tabColor"/>, in white or
    /// black, whichever reads better on it.</summary>
    public static byte[] RenderIcon(string initials, string tabColor)
    {
        var back = ColorTranslator.FromHtml(tabColor);
        var luminance = (0.299 * back.R + 0.587 * back.G + 0.114 * back.B) / 255;
        var fore = luminance > 0.6 ? Color.Black : Color.White;

        using var bitmap = new Bitmap(IconSize, IconSize, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            const float radius = IconSize / 5f;
            using (var square = new GraphicsPath())
            {
                var d = radius * 2;
                square.AddArc(0, 0, d, d, 180, 90);
                square.AddArc(IconSize - d - 1, 0, d, d, 270, 90);
                square.AddArc(IconSize - d - 1, IconSize - d - 1, d, d, 0, 90);
                square.AddArc(0, IconSize - d - 1, d, d, 90, 90);
                square.CloseFigure();
                using var fill = new SolidBrush(back);
                g.FillPath(fill, square);
            }

            var text = string.IsNullOrWhiteSpace(initials) ? "?" : initials;
            var size = text.Length >= 3 ? IconSize * 0.34f : IconSize * 0.44f;
            using var font = new Font("Segoe UI", size, FontStyle.Bold, GraphicsUnit.Pixel);
            using var brush = new SolidBrush(fore);
            using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(text, font, brush, new RectangleF(0, 0, IconSize, IconSize), format);
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    private static string Key(LoginLook look) => (look.Email ?? look.Window).Trim().ToLowerInvariant();

    private static string FileKey(LoginLook look) => FileKey(Key(look));

    private static string FileKey(string text)
    {
        var safe = new string(text.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' or '@' ? c : '_').ToArray()).Trim('.');
        return safe.Length == 0 ? "login" : safe;
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
