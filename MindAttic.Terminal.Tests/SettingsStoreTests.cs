using MindAttic.Terminal.Models;
using MindAttic.Terminal.Services;
using MindAttic.Vault.Credentials;
using MindAttic.Vault.Settings;
using NUnit.Framework;

namespace MindAttic.Terminal.Tests;

[TestFixture]
public sealed class SettingsStoreTests
{
    private string tempRoot = "";
    private SettingsStore subject = null!;
    private TokenStore tokens = null!;

    [SetUp]
    public void SetUp()
    {
        tempRoot = Path.Combine(Path.GetTempPath(), "MindAttic.Terminal.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        var store = new JsonSettingsStore<AppSettings>(Path.Combine(tempRoot, "settings"));
        tokens = new TokenStore(Path.Combine(tempRoot, "tokens"));
        subject = new SettingsStore(store, tokens);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(tempRoot, recursive: true); }
        catch { }
    }

    [Test]
    public void Load_returns_defaults_when_vault_is_empty()
    {
        var settings = subject.Load();

        Assert.That(settings, Is.Not.Null);
        Assert.That(settings.Projects, Is.Empty);
        Assert.That(settings.AgentProviders, Is.Empty);
        Assert.That(settings.Mobile.Enabled, Is.False);
        Assert.That(settings.Mobile.Token, Is.Null);
    }

    [Test]
    public void Save_persists_settings_round_trip()
    {
        var input = new AppSettings
        {
            Provider = "Claude",
            Projects =
            {
                new Project { Name = "Alpha", Path = @"C:\a", Provider = "Claude" },
                new Project { Name = "Beta",  Path = @"C:\b" }
            }
        };

        subject.Save(input);
        var roundTripped = subject.Load();

        Assert.That(roundTripped.Provider, Is.EqualTo("Claude"));
        Assert.That(roundTripped.Projects, Has.Count.EqualTo(2));
        Assert.That(roundTripped.Projects[0].Name, Is.EqualTo("Alpha"));
    }

    [Test]
    public void Save_never_writes_Mobile_Token_to_disk()
    {
        subject.Save(new AppSettings
        {
            Mobile = new MobileBridgeSettings { Token = "should-not-be-persisted", Enabled = true }
        });

        var raw = File.ReadAllText(subject.SettingsFilePath);
        Assert.That(raw, Does.Not.Contain("should-not-be-persisted"));
    }

    [Test]
    public void Load_overlays_Mobile_Token_from_vault_bucket()
    {
        subject.Save(new AppSettings { Mobile = new MobileBridgeSettings { Enabled = true } });
        tokens.Set(SettingsStore.MobileTokenKey, "vault-token-value");

        var loaded = subject.Load();

        Assert.That(loaded.Mobile.Token, Is.EqualTo("vault-token-value"));
    }

    [Test]
    public void GetMobileToken_returns_what_SetMobileToken_persisted()
    {
        subject.SetMobileToken("hello");
        Assert.That(subject.GetMobileToken(), Is.EqualTo("hello"));

        subject.SetMobileToken(null);
        Assert.That(subject.GetMobileToken(), Is.Null);
    }

    [Test]
    public void Load_seeds_from_legacy_file_when_vault_is_empty_and_moves_token_into_vault_bucket()
    {
        var legacyPath = Path.Combine(tempRoot, "legacy-settings.json");
        File.WriteAllText(legacyPath, """
        {
            "Provider": "Claude",
            "Mobile": {
                "ServerUrl": "http://127.0.0.1:7780",
                "Token": "legacy-plaintext-token",
                "Enabled": true,
                "AllProjects": true
            },
            "AgentProviders": [
                { "Key": "Claude", "Name": "Claude Code", "RunCommand": "claude" }
            ],
            "Projects": [
                { "Name": "Alpha", "Path": "C:\\a", "Provider": "Claude" }
            ]
        }
        """);

        var freshStore = new JsonSettingsStore<AppSettings>(Path.Combine(tempRoot, "fresh-settings"));
        var freshTokens = new TokenStore(Path.Combine(tempRoot, "fresh-tokens"));
        var seeded = new SettingsStore(freshStore, freshTokens, legacyPath);

        var loaded = seeded.Load();

        Assert.That(loaded.Provider, Is.EqualTo("Claude"));
        Assert.That(loaded.Mobile.Enabled, Is.True);
        Assert.That(loaded.Projects, Has.Count.EqualTo(1));
        Assert.That(loaded.Mobile.Token, Is.EqualTo("legacy-plaintext-token"));

        var raw = File.ReadAllText(freshStore.FilePath);
        Assert.That(raw, Does.Not.Contain("legacy-plaintext-token"));
        Assert.That(freshTokens.Get(SettingsStore.MobileTokenKey), Is.EqualTo("legacy-plaintext-token"));
    }
}
