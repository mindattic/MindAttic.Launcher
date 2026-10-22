using System.Diagnostics;
using System.IO.Compression;

namespace MindAttic.Launcher.Services;

public sealed record AppDataBackupResult(bool Ok, string ZipPath, long Bytes, TimeSpan Elapsed, string Output = "");

/// <summary>
/// Zips <c>%APPDATA%\MindAttic</c> (MindAttic.Vault's settings and credential
/// tree — FTP/LLM keyrings, every app's settings.json) into
/// <c>{targetFolder}\AppData.zip</c>, and restores the reverse direction. A
/// robocopy of the workspace plus SQL/SQLite backups never touches this tree,
/// so without it a restore would come back with every repo and database but
/// none of Vault's settings or credentials.
/// </summary>
public sealed class AppDataBackupService
{
    public const string ZipFileName = "AppData.zip";

    public static readonly string DefaultSource = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MindAttic");

    public string Source { get; }
    public Func<string, bool> Exists { get; }

    public AppDataBackupService() : this(DefaultSource) { }

    public AppDataBackupService(string source, Func<string, bool>? exists = null)
    {
        Source = source;
        Exists = exists ?? Directory.Exists;
    }

    public static string ResolveZipPath(string targetFolder) => Path.Combine(targetFolder, ZipFileName);

    /// <summary>
    /// Zips <see cref="Source"/> into <c>{targetFolder}\AppData.zip</c>. A
    /// missing source folder (nothing has ever seeded Vault settings on this
    /// machine) is reported OK-with-a-note rather than a failure — same
    /// warn-and-continue posture as every other best-effort step in this app.
    /// </summary>
    public AppDataBackupResult Zip(string targetFolder)
    {
        var zipPath = ResolveZipPath(targetFolder);
        if (!Exists(Source))
            return new AppDataBackupResult(true, zipPath, 0, TimeSpan.Zero, $"AppData folder not found — skipped: {Source}");

        var sw = Stopwatch.StartNew();
        try
        {
            Directory.CreateDirectory(targetFolder);
            if (File.Exists(zipPath)) File.Delete(zipPath);
            ZipFile.CreateFromDirectory(Source, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);
            sw.Stop();
            return new AppDataBackupResult(true, zipPath, new FileInfo(zipPath).Length, sw.Elapsed);
        }
        catch (Exception ex)
        {
            sw.Stop();
            // A zip that died midway (locked file, full disk) is a corrupt archive; don't leave it looking like a backup.
            try { if (File.Exists(zipPath)) File.Delete(zipPath); } catch { /* best-effort */ }
            return new AppDataBackupResult(false, zipPath, 0, sw.Elapsed, ex.Message);
        }
    }

    /// <summary>Extracts a previously-made AppData.zip back onto <paramref name="destDir"/>, overwriting in place.</summary>
    public AppDataBackupResult Restore(string zipPath, string destDir)
    {
        if (!File.Exists(zipPath))
            return new AppDataBackupResult(false, zipPath, 0, TimeSpan.Zero, $"Zip not found: {zipPath}");

        var sw = Stopwatch.StartNew();
        try
        {
            Directory.CreateDirectory(destDir);
            ZipFile.ExtractToDirectory(zipPath, destDir, overwriteFiles: true);
            sw.Stop();
            return new AppDataBackupResult(true, zipPath, new FileInfo(zipPath).Length, sw.Elapsed);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new AppDataBackupResult(false, zipPath, 0, sw.Elapsed, ex.Message);
        }
    }
}
