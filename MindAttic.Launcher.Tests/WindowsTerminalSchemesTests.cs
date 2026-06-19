using System.Text.Json;
using MindAttic.Launcher.Services;
using NUnit.Framework;

namespace MindAttic.Launcher.Tests;

[TestFixture]
public sealed class WindowsTerminalSchemesTests
{
    private const string Populated =
        """
        {
            "defaultProfile": "{guid}",
            "schemes":
            [
                {
                    "name": "MindAttic-Console",
                    "background": "#0A1929",
                    "foreground": "#CCCCCC"
                }
            ],
            "themes": []
        }
        """;

    private const string EmptySchemes =
        """
        {
            "schemes": [],
            "themes": []
        }
        """;

    [Test]
    public void Insert_adds_scheme_preserving_existing_and_staying_valid_json()
    {
        var ok = WindowsTerminalSchemes.TryInsertScheme(Populated, "MindAttic-Auth", "#071408", out var result);

        Assert.That(ok, Is.True);
        // Existing scheme survives.
        Assert.That(result, Does.Contain("MindAttic-Console"));
        // New scheme present with the requested background.
        Assert.That(result, Does.Contain("\"name\": \"MindAttic-Auth\""));
        Assert.That(result, Does.Contain("\"background\": \"#071408\""));
        // The whole document still parses.
        Assert.DoesNotThrow(() => JsonDocument.Parse(result));

        using var doc = JsonDocument.Parse(result);
        var names = doc.RootElement.GetProperty("schemes").EnumerateArray()
            .Select(s => s.GetProperty("name").GetString())
            .ToList();
        Assert.That(names, Is.EquivalentTo(new[] { "MindAttic-Auth", "MindAttic-Console" }));
    }

    [Test]
    public void Insert_is_idempotent_when_scheme_already_present()
    {
        WindowsTerminalSchemes.TryInsertScheme(Populated, "MindAttic-Auth", "#071408", out var once);
        var ok = WindowsTerminalSchemes.TryInsertScheme(once, "MindAttic-Auth", "#071408", out var twice);

        Assert.That(ok, Is.True);
        Assert.That(twice, Is.EqualTo(once));
        using var doc = JsonDocument.Parse(twice);
        var count = doc.RootElement.GetProperty("schemes").EnumerateArray()
            .Count(s => s.GetProperty("name").GetString() == "MindAttic-Auth");
        Assert.That(count, Is.EqualTo(1));
    }

    [Test]
    public void Insert_handles_an_empty_schemes_array()
    {
        var ok = WindowsTerminalSchemes.TryInsertScheme(EmptySchemes, "MindAttic-Auth", "#071408", out var result);

        Assert.That(ok, Is.True);
        Assert.DoesNotThrow(() => JsonDocument.Parse(result));
        using var doc = JsonDocument.Parse(result);
        var names = doc.RootElement.GetProperty("schemes").EnumerateArray()
            .Select(s => s.GetProperty("name").GetString())
            .ToList();
        Assert.That(names, Is.EqualTo(new[] { "MindAttic-Auth" }));
    }

    [Test]
    public void Insert_ignores_a_same_named_profile_outside_the_schemes_array()
    {
        // A WT profile named the same as the scheme we're adding. The old check
        // ("does the file contain this name anywhere?") would treat the scheme as
        // already present and skip the write while reporting success, leaving the
        // tab with no matching scheme. The scheme must still be inserted.
        const string profileCollision =
            """
            {
                "profiles": { "list": [ { "name": "MindAttic-Auth" } ] },
                "schemes":
                [
                    {
                        "name": "MindAttic-Console",
                        "background": "#0A1929",
                        "foreground": "#CCCCCC"
                    }
                ]
            }
            """;

        var ok = WindowsTerminalSchemes.TryInsertScheme(profileCollision, "MindAttic-Auth", "#071408", out var result);

        Assert.That(ok, Is.True);
        Assert.DoesNotThrow(() => JsonDocument.Parse(result));
        using var doc = JsonDocument.Parse(result);
        var schemeNames = doc.RootElement.GetProperty("schemes").EnumerateArray()
            .Select(s => s.GetProperty("name").GetString())
            .ToList();
        Assert.That(schemeNames, Is.EquivalentTo(new[] { "MindAttic-Auth", "MindAttic-Console" }));
    }

    [Test]
    public void Insert_returns_false_when_no_schemes_array()
    {
        var ok = WindowsTerminalSchemes.TryInsertScheme("{ \"profiles\": [] }", "MindAttic-Auth", "#071408", out var result);

        Assert.That(ok, Is.False);
        Assert.That(result, Is.EqualTo("{ \"profiles\": [] }"));
    }
}
