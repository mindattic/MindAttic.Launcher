using MindAttic.Launcher.Services;
using NUnit.Framework;

namespace MindAttic.Launcher.Tests;

[TestFixture]
public sealed class GitServiceTests
{
    [Test]
    public void Empty_porcelain_is_clean()
    {
        var status = GitService.ParseStatus("");
        Assert.That(status.IsClean, Is.True);
        Assert.That(status.IsValid, Is.True);
        Assert.That(status.Short(), Is.EqualTo("clean"));
    }

    [Test]
    public void Status_with_error_is_not_clean_and_short_returns_error()
    {
        var status = new GitStatus([], "PATH NOT FOUND");
        Assert.That(status.IsClean, Is.False, "an error state must not look clean");
        Assert.That(status.IsValid, Is.False);
        Assert.That(status.Short(), Is.EqualTo("PATH NOT FOUND"));
    }

    [Test]
    public void Status_for_missing_directory_returns_error_state()
    {
        var status = new GitService().Status(@"Z:\definitely\does\not\exist\here");
        Assert.That(status.IsValid, Is.False);
        Assert.That(status.Error, Is.EqualTo("PATH NOT FOUND"));
    }

    [Test]
    public void Untracked_files_count_as_added()
    {
        var status = GitService.ParseStatus("?? new.txt\n");
        Assert.That(status.Added, Is.EqualTo(1));
        Assert.That(status.Modified, Is.EqualTo(0));
        Assert.That(status.Deleted, Is.EqualTo(0));
    }

    [Test]
    public void Status_codes_classified_correctly()
    {
        var status = GitService.ParseStatus(string.Join("\n",
            " M modified.txt",
            "M  staged-modified.txt",
            "MM both-modified.txt",
            "A  staged-add.txt",
            " D worktree-delete.txt",
            "D  staged-delete.txt",
            "?? untracked.txt"));

        Assert.That(status.Total,    Is.EqualTo(7));
        Assert.That(status.Added,    Is.EqualTo(2), "untracked + staged-add");
        Assert.That(status.Modified, Is.EqualTo(3), "M-/-M/MM all modified");
        Assert.That(status.Deleted,  Is.EqualTo(2));
    }

    [Test]
    public void Rename_picks_up_destination_filename()
    {
        var status = GitService.ParseStatus("R  old/path/file.cs -> new/path/file.cs\n");
        Assert.That(status.Total, Is.EqualTo(1));
        Assert.That(status.Changes[0].FileName, Is.EqualTo("file.cs"));
    }

    [Test]
    public void Quoted_paths_are_unwrapped()
    {
        var status = GitService.ParseStatus("?? \"file with spaces.txt\"\n");
        Assert.That(status.Changes[0].FileName, Is.EqualTo("file with spaces.txt"));
    }

    [Test]
    public void AutoMessage_groups_by_action()
    {
        var status = GitService.ParseStatus(string.Join("\n",
            "?? newA.cs",
            "?? newB.cs",
            " M edited.cs",
            " D gone.cs"));

        Assert.That(status.AutoMessage(),
            Is.EqualTo("Add newA.cs, newB.cs; Update edited.cs; Remove gone.cs"));
    }

    [Test]
    public void AutoMessage_truncates_to_summary_when_over_limit()
    {
        var porcelain = string.Join("\n",
            Enumerable.Range(0, 50).Select(i => $"?? file_with_a_long_name_{i:D2}.cs"));
        var status = GitService.ParseStatus(porcelain);

        Assert.That(status.AutoMessage(), Is.EqualTo("Add 50 file(s)"));
    }

    [Test]
    public void Short_format_uses_compact_counts()
    {
        var status = GitService.ParseStatus(string.Join("\n",
            "?? a.txt",
            "?? b.txt",
            " M c.txt",
            " D d.txt"));

        Assert.That(status.Short(), Is.EqualTo("4 change(s) [+2 ~1 -1]"));
    }
}
