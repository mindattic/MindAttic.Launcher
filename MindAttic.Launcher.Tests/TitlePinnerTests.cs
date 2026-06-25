using MindAttic.Launcher.Services;
using NUnit.Framework;

namespace MindAttic.Launcher.Tests;

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

    // Claude Code's footer shows "N shell"/"N shells" while background shells
    // it spawned are still running — the agent prompt is idle then, but work
    // continues, so the title should stay on the play glyph.
    [Test]
    public void HasBackgroundShell_matches_a_running_background_shell()
    {
        Assert.Multiple(() =>
        {
            Assert.That(TitlePinner.HasBackgroundShell("> \n  1 shell · ? for shortcuts"), Is.True);
            Assert.That(TitlePinner.HasBackgroundShell("3 SHELLS running"), Is.True);
            Assert.That(TitlePinner.HasBackgroundShell("background shell"), Is.True);
        });
    }

    [Test]
    public void HasBackgroundShell_is_false_for_an_idle_prompt_with_no_shells()
    {
        Assert.Multiple(() =>
        {
            Assert.That(TitlePinner.HasBackgroundShell(null), Is.False);
            Assert.That(TitlePinner.HasBackgroundShell("> \n  ? for shortcuts"), Is.False);
            // "shells?" is word-bounded, so the substring inside "shellfish"/"PowerShell" stays quiet.
            Assert.That(TitlePinner.HasBackgroundShell("shellfish"), Is.False);
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
