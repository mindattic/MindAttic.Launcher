using MindAttic.Launcher.Services;
using NUnit.Framework;

namespace MindAttic.Launcher.Tests;

[TestFixture]
public sealed class GitHubRepoRefTests
{
    [TestCase("https://github.com/mindattic/MindAttic.Launcher", ExpectedResult = "mindattic/mindattic.launcher")]
    [TestCase("https://github.com/mindattic/MindAttic.Launcher.git", ExpectedResult = "mindattic/mindattic.launcher")]
    [TestCase("git@github.com:mindattic/MindAttic.Launcher.git", ExpectedResult = "mindattic/mindattic.launcher")]
    [TestCase("git@github.com:mindattic/MindAttic.Launcher", ExpectedResult = "mindattic/mindattic.launcher")]
    [TestCase("https://github.com/mindattic/MindAttic.Launcher/", ExpectedResult = "mindattic/mindattic.launcher")]
    public string? Parse_normalizes_github_remotes_to_lowercase_owner_slash_repo(string remoteUrl) =>
        GitHubRepoRef.Parse(remoteUrl);

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("https://gitlab.com/owner/repo")]
    [TestCase("https://github.com/just-an-owner")]
    [TestCase("not a url")]
    public void Parse_returns_null_for_blank_non_github_or_unparseable_remotes(string? remoteUrl) =>
        Assert.That(GitHubRepoRef.Parse(remoteUrl), Is.Null);
}
