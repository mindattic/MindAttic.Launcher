using MindAttic.Launcher.Services;
using NUnit.Framework;

namespace MindAttic.Launcher.Tests;

[TestFixture]
public sealed class GitServicePullTests
{
    [TestCase("There is no tracking information for the current branch.\nPlease specify which branch you want to merge with.")]
    [TestCase("fatal: No remote repository specified.  Please, specify either a URL or a\nremote name from which new revisions should be fetched.")]
    [TestCase("fatal: 'origin' does not appear to be a git repository\nfatal: Could not read from remote repository.")]
    public void IsNoUpstream_is_true_for_a_repo_with_nothing_to_pull_from(string message)
    {
        Assert.That(GitService.IsNoUpstream(message), Is.True);
    }

    [TestCase("error: Your local changes to the following files would be overwritten by merge:\n\tREADME.md")]
    [TestCase("fatal: Not possible to fast-forward, aborting.")]
    [TestCase("fatal: unable to access 'https://github.com/mindattic/X.git/': Could not resolve host: github.com")]
    [TestCase("git pull failed (exit 1)")]
    [TestCase(null)]
    public void IsNoUpstream_is_false_for_real_pull_failures(string? message)
    {
        Assert.That(GitService.IsNoUpstream(message), Is.False);
    }
}
