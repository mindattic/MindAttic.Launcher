using MindAttic.Launcher.Services;
using NUnit.Framework;

namespace MindAttic.Launcher.Tests;

[TestFixture]
public sealed class ClaudeAuthStatusTests
{
    private string dir = "";
    private string credentialsPath = "";

    [SetUp]
    public void SetUp()
    {
        dir = Path.Combine(Path.GetTempPath(), "MindAttic.Launcher.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        credentialsPath = Path.Combine(dir, ".credentials.json");
    }

    [TearDown]
    public void TearDown() => Directory.Delete(dir, recursive: true);

    [Test]
    public void IsLoggedIntoClaudeAi_is_false_when_the_file_is_missing() =>
        Assert.That(ClaudeAuthStatus.IsLoggedIntoClaudeAi(Path.Combine(dir, "nope.json")), Is.False);

    [Test]
    public void IsLoggedIntoClaudeAi_is_true_with_a_non_empty_access_token()
    {
        File.WriteAllText(credentialsPath, """{ "claudeAiOauth": { "accessToken": "fake-session-token" } }""");
        Assert.That(ClaudeAuthStatus.IsLoggedIntoClaudeAi(credentialsPath), Is.True);
    }

    [Test]
    public void IsLoggedIntoClaudeAi_is_false_with_a_blank_access_token()
    {
        File.WriteAllText(credentialsPath, """{ "claudeAiOauth": { "accessToken": "" } }""");
        Assert.That(ClaudeAuthStatus.IsLoggedIntoClaudeAi(credentialsPath), Is.False);
    }

    [Test]
    public void IsLoggedIntoClaudeAi_is_false_without_a_claudeAiOauth_block()
    {
        File.WriteAllText(credentialsPath, """{ "someOtherKey": true }""");
        Assert.That(ClaudeAuthStatus.IsLoggedIntoClaudeAi(credentialsPath), Is.False);
    }

    [Test]
    public void IsLoggedIntoClaudeAi_is_false_for_malformed_json()
    {
        File.WriteAllText(credentialsPath, "{ not valid json");
        Assert.That(ClaudeAuthStatus.IsLoggedIntoClaudeAi(credentialsPath), Is.False);
    }
}
