using MindAttic.Console.Services;
using NUnit.Framework;

namespace MindAttic.Console.Tests;

[TestFixture]
public sealed class TitlePinnerTests
{
    [Test]
    public void LooksBusy_is_false_for_null_or_empty()
    {
        Assert.That(TitlePinner.LooksBusy(null), Is.False);
        Assert.That(TitlePinner.LooksBusy(""), Is.False);
    }

    [Test]
    public void LooksBusy_is_false_for_an_idle_prompt()
    {
        Assert.That(TitlePinner.LooksBusy("> \n  ? for shortcuts"), Is.False);
    }

    // Claude Code renders "(esc to interrupt)" in its working footer.
    [Test]
    public void LooksBusy_matches_the_Claude_working_footer()
    {
        Assert.That(TitlePinner.LooksBusy("✶ Cogitating… (12s · esc to interrupt)"), Is.True);
    }

    // Codex renders the same "esc to interrupt" inside its "Working" footer.
    [Test]
    public void LooksBusy_matches_the_Codex_working_footer()
    {
        Assert.That(TitlePinner.LooksBusy("• Working (1s • esc to interrupt)"), Is.True);
    }

    [Test]
    public void LooksBusy_matches_each_busy_pattern_case_insensitively()
    {
        Assert.Multiple(() =>
        {
            Assert.That(TitlePinner.LooksBusy("ESC TO INTERRUPT"), Is.True);
            Assert.That(TitlePinner.LooksBusy("press Esc to cancel"), Is.True);
            Assert.That(TitlePinner.LooksBusy("Ctrl+C to interrupt"), Is.True);
            Assert.That(TitlePinner.LooksBusy("ctrl+c to cancel"), Is.True);
        });
    }

    [Test]
    public void Compose_uses_the_play_glyph_when_busy()
    {
        Assert.That(TitlePinner.Compose(isBusy: true, "Legion [Claude]"), Is.EqualTo("▶  Legion [Claude]"));
    }

    [Test]
    public void Compose_uses_the_pause_glyph_when_idle()
    {
        Assert.That(TitlePinner.Compose(isBusy: false, "Legion [Codex]"), Is.EqualTo("⏸  Legion [Codex]"));
    }
}
