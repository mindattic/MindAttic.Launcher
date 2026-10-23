using MindAttic.Launcher.Menus;
using MindAttic.Launcher.Models;
using MindAttic.Tickets;
using MindAttic.Tickets.Repos;
using NUnit.Framework;

namespace MindAttic.Launcher.Tests;

/// <summary>The logic-bearing helpers inside the menus (M.LNCH-T20), exposed as internal static.</summary>
[TestFixture]
public sealed class MenuHelperTests
{
    [TestCase("")]
    [TestCase("abc")]
    [TestCase("12345678")]
    public void Mask_fully_masks_8_chars_or_fewer(string token) =>
        Assert.That(SettingsMenu.Mask(token), Is.EqualTo("••••"));

    [Test]
    public void Mask_keeps_first4_and_last4_beyond_8_chars() =>
        Assert.That(SettingsMenu.Mask("ghp_abcdefghij1234"), Is.EqualTo("ghp_…1234"));

    [Test]
    public void Mask_9_chars_is_the_first_unmasked_length() =>
        Assert.That(SettingsMenu.Mask("123456789"), Is.EqualTo("1234…6789"));

    private static RepoStatus Status(string? slug, string? error = null) =>
        new(new Repo("X", "C:/x"), "https://example.com/x", slug!, 0, 0, 0, [], error!);

    private static GitHubRepoInfo Info(bool archived, params string[] topics) => new("o/r", archived, topics);

    [Test]
    public void GitHubCell_git_error() =>
        Assert.That(TicketsMenu.GitHubCell(Status("o/r", "boom"), null), Does.Contain("git error"));

    [Test]
    public void GitHubCell_no_github_remote() =>
        Assert.That(TicketsMenu.GitHubCell(Status(null), null), Does.Contain("no GitHub remote"));

    [Test]
    public void GitHubCell_unknown_when_index_missing() =>
        Assert.That(TicketsMenu.GitHubCell(Status("o/r"), null), Does.Contain("?"));

    [Test]
    public void GitHubCell_not_found() =>
        Assert.That(TicketsMenu.GitHubCell(Status("o/r"), new Dictionary<string, GitHubRepoInfo>()), Does.Contain("not found"));

    [Test]
    public void GitHubCell_archived_beats_wip()
    {
        var idx = new Dictionary<string, GitHubRepoInfo> { ["o/r"] = Info(true, GitHubRepoIndex.WorkInProgressTopic) };
        Assert.That(TicketsMenu.GitHubCell(Status("o/r"), idx), Does.Contain("archived"));
    }

    [Test]
    public void GitHubCell_wip_vs_untagged()
    {
        var wip = new Dictionary<string, GitHubRepoInfo> { ["o/r"] = Info(false, GitHubRepoIndex.WorkInProgressTopic) };
        var bare = new Dictionary<string, GitHubRepoInfo> { ["o/r"] = Info(false) };
        Assert.That(TicketsMenu.GitHubCell(Status("o/r"), wip), Does.Contain("wip"));
        Assert.That(TicketsMenu.GitHubCell(Status("o/r"), bare), Does.Contain("untagged"));
    }

    [Test]
    public void UsedColors_first_project_wins_case_insensitively_and_blank_ignored()
    {
        var settings = new AppSettings
        {
            Projects =
            [
                new Project { Name = "A", TabColor = "#AABBCC" },
                new Project { Name = "B", TabColor = "#aabbcc" },
                new Project { Name = "C", TabColor = "" },
                new Project { Name = "D", TabColor = null },
                new Project { Name = "E", TabColor = "#112233" },
            ],
        };
        var map = DiscoverProjectsMenu.UsedColors(settings);
        Assert.That(map, Has.Count.EqualTo(2));
        Assert.That(map["#aabbcc"], Is.EqualTo("A"));
        Assert.That(map["#112233"], Is.EqualTo("E"));
    }

    [TestCase(0L, "0 B")]
    [TestCase(1023L, "1023 B")]
    [TestCase(1024L, "1.00 KB")]
    [TestCase(1024L * 1024 - 1, "1024.00 KB")]
    [TestCase(1024L * 1024, "1.00 MB")]
    [TestCase(1024L * 1024 * 1024, "1.00 GB")]
    public void FormatBytes_boundaries(long bytes, string expected) =>
        Assert.That(BackupMenu.FormatBytes(bytes).Replace(',', '.'), Is.EqualTo(expected));

    [TestCase(0, 0, 59, "0:59")]
    [TestCase(0, 1, 5, "1:05")]
    [TestCase(1, 0, 0, "1:00:00")]
    [TestCase(2, 3, 4, "2:03:04")]
    public void Format_switches_to_hours_at_one_hour(int h, int m, int s, string expected) =>
        Assert.That(BackupMenu.Format(new TimeSpan(h, m, s)), Is.EqualTo(expected));
}
