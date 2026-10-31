using MindAttic.Launcher.Services;
using MindAttic.Vault.Credentials;
using NUnit.Framework;

namespace MindAttic.Launcher.Tests;

/// <summary>
/// M.LNCH-T95: the Claude key the Launcher puts on a CLI is the key the Orchestrator bills when keys are assigned to the
/// Orchestrator for Claude in Vault; with no such assignment, the Launcher's own active key, as before.
/// </summary>
[TestFixture]
public sealed class ProviderCredentialsKeyTests
{
    private string root = "";
    private NamedKeyStore keys = null!;

    [SetUp]
    public void SetUp()
    {
        root = Path.Combine(Path.GetTempPath(), "launcher-keys-" + Guid.NewGuid().ToString("N"));
        keys = new NamedKeyStore(root, new KeyAssignmentStore(Path.Combine(root, KeyAssignmentStore.Bucket)));
        keys.Add("claude", "first", "sk-first");
        keys.Add("claude", "second", "sk-second");
        keys.Add("gemini", "only", "gm-key");
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(root, recursive: true); } catch (IOException) { }
    }

    [Test]
    public void Claude_NoOrchestratorAssignment_UsesTheSharedOrder() =>
        Assert.That(ProviderCredentials.ResolveKey("Claude", keys), Is.EqualTo("sk-first"));

    [Test]
    public void Claude_OrchestratorAssignment_UsesTheOrchestratorsKey()
    {
        keys.Assignments.Set(ProviderCredentials.OrchestratorAppId, "claude", ["second"]);
        Assert.That(ProviderCredentials.ResolveKey("Claude", keys), Is.EqualTo("sk-second"));
    }

    [Test]
    public void Claude_OrchestratorAssignment_WinsOverTheLaunchersOwn()
    {
        keys.Assignments.Set(SettingsStore.AppBucket, "claude", ["first"]);
        keys.Assignments.Set(ProviderCredentials.OrchestratorAppId, "claude", ["second"]);
        Assert.That(ProviderCredentials.ResolveKey("Claude", keys), Is.EqualTo("sk-second"));
    }

    [Test]
    public void Claude_LaunchersOwnAssignment_StillAppliesWithoutAnOrchestratorOne()
    {
        keys.Assignments.Set(SettingsStore.AppBucket, "claude", ["second"]);
        Assert.That(ProviderCredentials.ResolveKey("Claude", keys), Is.EqualTo("sk-second"));
    }

    [Test]
    public void OtherProviders_IgnoreTheOrchestratorsClaudeAssignment()
    {
        keys.Assignments.Set(ProviderCredentials.OrchestratorAppId, "claude", ["second"]);
        Assert.That(ProviderCredentials.ResolveKey("Gemini", keys), Is.EqualTo("gm-key"));
    }
}
