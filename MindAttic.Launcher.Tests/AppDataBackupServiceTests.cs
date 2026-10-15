using MindAttic.Launcher.Services;
using NUnit.Framework;

namespace MindAttic.Launcher.Tests;

[TestFixture]
public sealed class AppDataBackupServiceTests
{
    private string tempFolder = "";

    [SetUp]
    public void SetUp()
    {
        tempFolder = Path.Combine(Path.GetTempPath(), "MindAtticAppDataBackupTests", TestContext.CurrentContext.Test.ID);
        Directory.CreateDirectory(tempFolder);
    }

    [TearDown]
    public void TearDown()
    {
        try { if (Directory.Exists(tempFolder)) Directory.Delete(tempFolder, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    [Test]
    public void ResolveZipPath_places_AppData_zip_directly_under_the_target_folder()
    {
        Assert.That(AppDataBackupService.ResolveZipPath(@"R:\Backup\MindAttic\2026-05-23"),
            Is.EqualTo(@"R:\Backup\MindAttic\2026-05-23\AppData.zip"));
    }

    [Test]
    public void Zip_reports_ok_without_writing_a_file_when_the_source_folder_is_missing()
    {
        var subject = new AppDataBackupService(Path.Combine(tempFolder, "DoesNotExist"), exists: _ => false);

        var result = subject.Zip(Path.Combine(tempFolder, "target"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.True);
            Assert.That(result.Bytes, Is.EqualTo(0));
            Assert.That(File.Exists(result.ZipPath), Is.False);
            Assert.That(result.Output, Does.Contain("not found"));
        });
    }

    [Test]
    public void Zip_creates_a_real_zip_from_the_source_directory()
    {
        var source = Path.Combine(tempFolder, "AppData");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "settings.json"), "{}");
        var target = Path.Combine(tempFolder, "target");

        var subject = new AppDataBackupService(source);
        var result = subject.Zip(target);

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.True);
            Assert.That(File.Exists(result.ZipPath), Is.True);
            Assert.That(result.Bytes, Is.GreaterThan(0));
        });
    }

    [Test]
    public void Restore_extracts_a_zip_back_onto_the_destination_overwriting_in_place()
    {
        var source = Path.Combine(tempFolder, "AppData");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "settings.json"), "{\"a\":1}");
        var backupTarget = Path.Combine(tempFolder, "target");
        var subject = new AppDataBackupService(source);
        var zipResult = subject.Zip(backupTarget);

        var dest = Path.Combine(tempFolder, "restored");
        Directory.CreateDirectory(dest);
        File.WriteAllText(Path.Combine(dest, "stale.txt"), "old"); // pre-existing file must survive — extraction doesn't wipe the dir first

        var restoreResult = subject.Restore(zipResult.ZipPath, dest);

        Assert.Multiple(() =>
        {
            Assert.That(restoreResult.Ok, Is.True);
            Assert.That(File.Exists(Path.Combine(dest, "settings.json")), Is.True);
            Assert.That(File.ReadAllText(Path.Combine(dest, "settings.json")), Is.EqualTo("{\"a\":1}"));
        });
    }

    [Test]
    public void Restore_fails_cleanly_when_the_zip_does_not_exist()
    {
        var subject = new AppDataBackupService(tempFolder);
        var result = subject.Restore(Path.Combine(tempFolder, "missing.zip"), Path.Combine(tempFolder, "dest"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.False);
            Assert.That(result.Output, Does.Contain("not found"));
        });
    }
}
