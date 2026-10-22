using MindAttic.Launcher.Services;
using NUnit.Framework;

namespace MindAttic.Launcher.Tests;

/// <summary><c>host --resume &lt;session&gt;</c> (M.LNCH-T64): which providers take it, and which login a resumed
/// conversation runs under when the caller names none.</summary>
[TestFixture]
public sealed class ClaudeResumeTests
{
    private const string Session = "11111111-2222-3333-4444-555555555555";
    private string dir = "";

    [SetUp]
    public void SetUp()
    {
        dir = Path.Combine(Path.GetTempPath(), "MindAttic.Launcher.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
    }

    private string Transcript(string configDir, string project = "D--Projects-Prose")
    {
        var folder = Path.Combine(configDir, "projects", project);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, Session + ".jsonl"), "{}");
        return configDir;
    }

    [Test]
    public void Validate_allows_no_resume_on_any_provider() =>
        Assert.That(ClaudeResume.Validate("Codex", null), Is.Null);

    [Test]
    public void Validate_allows_a_session_on_a_Claude_tab() =>
        Assert.That(ClaudeResume.Validate("claude", Session), Is.Null);

    [Test]
    public void Validate_rejects_resume_on_other_providers() =>
        Assert.That(ClaudeResume.Validate("Codex", Session), Does.Contain("only supported for Claude"));

    [TestCase("")]
    [TestCase("  ")]
    [TestCase(@"..\..\x")]
    [TestCase("a/b")]
    public void Validate_rejects_a_blank_or_path_like_session(string session) =>
        Assert.That(ClaudeResume.Validate("Claude", session), Is.Not.Null);

    [Test]
    public void FindConfigDir_returns_the_login_holding_the_transcript()
    {
        var global = Path.Combine(dir, "global");
        Directory.CreateDirectory(Path.Combine(global, "projects", "D--Projects-Prose"));
        var a = Path.Combine(dir, "a@x.com");
        var b = Transcript(Path.Combine(dir, "b@x.com"));

        Assert.That(ClaudeResume.FindConfigDir(Session, new[] { "", a, b }, global), Is.EqualTo(b));
    }

    [Test]
    public void FindConfigDir_maps_the_global_login_to_empty()
    {
        var global = Transcript(Path.Combine(dir, "global"));

        Assert.That(ClaudeResume.FindConfigDir(Session, new[] { "", Path.Combine(dir, "a@x.com") }, global), Is.EqualTo(""));
    }

    [Test]
    public void FindConfigDir_is_null_when_no_login_holds_it() =>
        Assert.That(ClaudeResume.FindConfigDir(Session, new[] { "", Path.Combine(dir, "a@x.com") }, Path.Combine(dir, "global")), Is.Null);

    [Test]
    public void SignedInLogins_lists_the_global_login_then_each_per_cli_login()
    {
        Directory.CreateDirectory(Path.Combine(dir, "b@x.com"));
        Directory.CreateDirectory(Path.Combine(dir, "a@x.com"));

        Assert.That(ClaudeResume.SignedInLogins(dir), Is.EqualTo(new[] { "", Path.Combine(dir, "a@x.com"), Path.Combine(dir, "b@x.com") }));
    }
}
