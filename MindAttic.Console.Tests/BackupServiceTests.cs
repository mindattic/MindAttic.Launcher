using MindAttic.Console.Services;
using NUnit.Framework;

namespace MindAttic.Console.Tests;

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

    [TestCase(0, ExpectedResult = true)]
    [TestCase(1, ExpectedResult = true)]
    [TestCase(7, ExpectedResult = true)]
    [TestCase(8, ExpectedResult = false)]
    [TestCase(16, ExpectedResult = false)]
    [TestCase(-1, ExpectedResult = false)]
    public bool ComputeOk_treats_only_0_through_7_as_success(int exitCode) =>
        BackupService.ComputeOk(exitCode, cancelled: false);

    [Test]
    public void ComputeOk_reports_failure_when_cancelled_even_on_a_success_code()
    {
        Assert.That(BackupService.ComputeOk(0, cancelled: true), Is.False);
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
