using MindAttic.Console.Services;
using NUnit.Framework;

namespace MindAttic.Console.Tests;

[TestFixture]
public sealed class ColorPaletteTests
{
    [TestCase("MindAttic.Authentication", ExpectedResult = "MindAttic-Authentication")]
    [TestCase("MindAttic.Vault", ExpectedResult = "MindAttic-Vault")]
    [TestCase("ChiMesh", ExpectedResult = "MindAttic-ChiMesh")]
    // The strip is case-insensitive (mirrors Project.TabTitle), so a lowercase
    // "mindattic." prefix is stripped too — "com" here, not "mindattic.com".
    [TestCase("mindattic.com", ExpectedResult = "MindAttic-com")]
    public string SchemeName_strips_the_MindAttic_prefix(string projectName) =>
        ColorPalette.SchemeName(projectName);

    [Test]
    public void DarkBackground_scales_each_channel_to_16_percent()
    {
        // #2E7D32 = (46,125,50); *0.16 rounds to (7,20,8) = #071408.
        Assert.That(ColorPalette.DarkBackground("#2E7D32"), Is.EqualTo("#071408"));
    }

    [Test]
    public void DarkBackground_handles_white_and_black_extremes()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ColorPalette.DarkBackground("#000000"), Is.EqualTo("#000000"));
            // 255 * 0.16 = 40.8 -> 41 = 0x29
            Assert.That(ColorPalette.DarkBackground("#FFFFFF"), Is.EqualTo("#292929"));
        });
    }

    [Test]
    public void DarkBackground_falls_back_to_black_on_junk_input()
    {
        Assert.That(ColorPalette.DarkBackground("not-a-color"), Is.EqualTo("#0C0C0C"));
    }

    [TestCase("2E7D32", "#2E7D32")]
    [TestCase("#2e7d32", "#2E7D32")]
    [TestCase("  #abcdef  ", "#ABCDEF")]
    public void TryNormalizeHex_accepts_and_canonicalizes(string input, string expected)
    {
        Assert.That(ColorPalette.TryNormalizeHex(input, out var hex), Is.True);
        Assert.That(hex, Is.EqualTo(expected));
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("12345")]      // too short
    [TestCase("1234567")]    // too long
    [TestCase("#GGGGGG")]    // not hex
    [TestCase(null)]
    public void TryNormalizeHex_rejects_invalid_input(string? input)
    {
        Assert.That(ColorPalette.TryNormalizeHex(input, out var hex), Is.False);
        Assert.That(hex, Is.Empty);
    }

    [Test]
    public void Palette_colors_are_all_valid_hex_and_uniquely_named()
    {
        Assert.Multiple(() =>
        {
            foreach (var c in ColorPalette.Colors)
                Assert.That(ColorPalette.TryNormalizeHex(c.Hex, out _), Is.True, $"{c.Name} has invalid hex {c.Hex}");
            Assert.That(ColorPalette.Colors.Select(c => c.Name).Distinct().Count(),
                Is.EqualTo(ColorPalette.Colors.Count), "palette names must be unique");
        });
    }
}
