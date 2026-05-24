using MindAttic.Terminal.Services;
using NUnit.Framework;

namespace MindAttic.Terminal.Tests;

[TestFixture]
public sealed class BackupServiceTests
{
    private static readonly DateTime FixedDate = new(2026, 5, 23);

    private static BackupService Make(HashSet<string> existing) =>
        new(@"D:\Projects\MindAttic",
            @"R:\Backup\MindAttic",
            exists: p => existing.Contains(p),
            now: () => FixedDate);

    [Test]
    public void ResolveTargetFolder_uses_dated_folder_when_unused()
    {
        var subject = Make([]);
        Assert.That(subject.ResolveTargetFolder(),
            Is.EqualTo(@"R:\Backup\MindAttic\2026-05-23"));
    }

    [Test]
    public void ResolveTargetFolder_appends_underscore_a_on_collision()
    {
        var subject = Make([@"R:\Backup\MindAttic\2026-05-23"]);
        Assert.That(subject.ResolveTargetFolder(),
            Is.EqualTo(@"R:\Backup\MindAttic\2026-05-23_a"));
    }

    [Test]
    public void ResolveTargetFolder_walks_to_first_available_letter()
    {
        var existing = new HashSet<string>
        {
            @"R:\Backup\MindAttic\2026-05-23",
            @"R:\Backup\MindAttic\2026-05-23_a",
            @"R:\Backup\MindAttic\2026-05-23_b",
            @"R:\Backup\MindAttic\2026-05-23_c"
        };
        var subject = Make(existing);
        Assert.That(subject.ResolveTargetFolder(),
            Is.EqualTo(@"R:\Backup\MindAttic\2026-05-23_d"));
    }

    [Test]
    public void ResolveTargetFolder_throws_when_all_27_slots_taken()
    {
        var existing = new HashSet<string> { @"R:\Backup\MindAttic\2026-05-23" };
        for (var c = 'a'; c <= 'z'; c++)
            existing.Add($@"R:\Backup\MindAttic\2026-05-23_{c}");
        var subject = Make(existing);

        Assert.Throws<InvalidOperationException>(() => subject.ResolveTargetFolder());
    }

    [Test]
    public void Exclude_lists_match_original_PS_launcher()
    {
        Assert.That(BackupService.ExcludeDirs, Is.EquivalentTo(new[]
        {
            "Library", "Temp", "Logs", "obj", "bin", "Build", "Builds",
            "node_modules", ".vs", ".idea", ".git"
        }));
        Assert.That(BackupService.ExcludeFiles, Is.EquivalentTo(new[] { "*.log", "*.tmp" }));
    }
}
