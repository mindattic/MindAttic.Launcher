using MindAttic.Terminal.Models;
using MindAttic.Terminal.Services;
using MindAttic.Vault.Credentials;
using MindAttic.Vault.Settings;
using NUnit.Framework;

namespace MindAttic.Terminal.Tests;

[TestFixture]
public sealed class AgentProviderRegistryTests
{
    private string tempRoot = "";
    private SettingsStore store = null!;
    private AgentProviderRegistry registry = null!;

    [SetUp]
    public void SetUp()
    {
        tempRoot = Path.Combine(Path.GetTempPath(), "MindAttic.Terminal.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var json = new JsonSettingsStore<AppSettings>(Path.Combine(tempRoot, "settings"));
        var tokens = new TokenStore(Path.Combine(tempRoot, "tokens"));
        store = new SettingsStore(json, tokens);
        registry = new AgentProviderRegistry(store);

        store.Save(new AppSettings
        {
            Provider = "Claude",
            AgentProviders =
            {
                new AgentProvider { Key = "Claude", Name = "Claude Code",  RunCommand = "claude --dangerously-skip-permissions" },
                new AgentProvider { Key = "Codex",  Name = "OpenAI Codex", RunCommand = "codex --dangerously-bypass-approvals-and-sandbox" }
            },
            Projects =
            {
                new Project { Name = "Alpha", Path = "C:\\a", Provider = "Codex" },
                new Project { Name = "Beta",  Path = "C:\\b" }
            }
        });
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(tempRoot, recursive: true); } catch { }
    }

    [Test]
    public void Defaults_are_returned_when_AgentProviders_is_empty()
    {
        store.Save(new AppSettings());
        Assert.That(new AgentProviderRegistry(store).All(), Has.Count.EqualTo(2));
    }

    [Test]
    public void EffectiveProvider_uses_project_override_when_set()
    {
        var p = ProjectRoster.FindByName(store.Load(), "Alpha")!;
        Assert.That(registry.EffectiveProvider(p).Key, Is.EqualTo("Codex"));
    }

    [Test]
    public void EffectiveProvider_falls_back_to_default_when_project_unset()
    {
        var p = ProjectRoster.FindByName(store.Load(), "Beta")!;
        Assert.That(registry.EffectiveProvider(p).Key, Is.EqualTo("Claude"));
    }

    [Test]
    public void Next_cycles_through_providers()
    {
        Assert.That(registry.Next("Claude").Key, Is.EqualTo("Codex"));
        Assert.That(registry.Next("Codex").Key, Is.EqualTo("Claude"));
    }

    [Test]
    public void SetDefault_persists_through_store_round_trip()
    {
        registry.SetDefault("Codex");
        Assert.That(new AgentProviderRegistry(store).CurrentDefaultKey(), Is.EqualTo("Codex"));
    }

    [Test]
    public void SetProjectProvider_null_clears_override()
    {
        registry.SetProjectProvider("Alpha", null);
        var p = ProjectRoster.FindByName(store.Load(), "Alpha")!;
        Assert.That(p.Provider, Is.Null);
        Assert.That(registry.EffectiveProvider(p).Key, Is.EqualTo("Claude"));
    }

    [Test]
    public void SetProjectProvider_unknown_key_throws()
    {
        Assert.Throws<ArgumentException>(() => registry.SetProjectProvider("Alpha", "Bogus"));
    }
}
