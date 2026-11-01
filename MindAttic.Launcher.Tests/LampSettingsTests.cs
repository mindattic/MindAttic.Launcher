using System.IO;
using MindAttic.Launcher.Menus;
using MindAttic.Launcher.Models;
using MindAttic.Launcher.Services;
using MindAttic.Vault.Paths;
using MindAttic.Vault.Settings;
using MindAttic.Vault.Settings.Manifest;
using NUnit.Framework;

namespace MindAttic.Launcher.Tests;

/// <summary>Every test in this assembly sees temp Vault roots, so nothing can write the real AppData.</summary>
[SetUpFixture]
public sealed class TestVaultRoots
{
    private string root = "";
    private string? roaming, local;

    [OneTimeSetUp]
    public void SetUp()
    {
        root = Path.Combine(Path.GetTempPath(), "LauncherVault-" + Guid.NewGuid().ToString("N"));
        roaming = Environment.GetEnvironmentVariable(VaultPaths.RoamingRootEnvVar);
        local = Environment.GetEnvironmentVariable(VaultPaths.LocalRootEnvVar);
        Environment.SetEnvironmentVariable(VaultPaths.RoamingRootEnvVar, Path.Combine(root, "roaming"));
        Environment.SetEnvironmentVariable(VaultPaths.LocalRootEnvVar, Path.Combine(root, "local"));
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable(VaultPaths.RoamingRootEnvVar, roaming);
        Environment.SetEnvironmentVariable(VaultPaths.LocalRootEnvVar, local);
        try { Directory.Delete(root, recursive: true); } catch { }
    }
}

/// <summary>LAMP (M.LNCH-T94): the Launcher's settings edited in Vault, one end-to-end test per setting.</summary>
[NonParallelizable]
public class LampSettingsTests
{
    private string dir = "";
    private SettingsStore Store(string? legacy = null) => new(new JsonSettingsStore<AppSettings>(Path.Combine(dir, "vault")), legacy);

    [SetUp]
    public void SetUp()
    {
        dir = Path.Combine(Path.GetTempPath(), "launcher-lamp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(dir, recursive: true); } catch { }
    }

    private void WriteToday(string json)
    {
        Directory.CreateDirectory(Path.Combine(dir, "vault"));
        File.WriteAllText(Path.Combine(dir, "vault", "settings.json"), json);
    }

    [Test]
    public void Setting_WindowsTerminalSettingsPath_ImportsRoundTripsAndIsWhereSchemesAreWritten()
    {
        var wt = Path.Combine(dir, "wt-settings.json");
        File.WriteAllText(wt, """{ "profiles": { "list": [] }, "schemes": [] }""");
        WriteToday($$"""{ "windowsTerminalSettingsPath": {{System.Text.Json.JsonSerializer.Serialize(wt)}} }""");
        var settings = Store().Load();
        Assert.That(settings.WindowsTerminalSettingsPath, Is.EqualTo(wt), "today's value kept");
        Assert.That(new WindowsTerminalSchemes().EnsureScheme(settings.WindowsTerminalSettingsPath, "MindAttic-Lamp", "#102030"), Is.True);
        Assert.That(File.ReadAllText(wt), Does.Contain("MindAttic-Lamp"));
    }

    [Test]
    public void Setting_AgentProviders_ImportRoundTripAndAreTheCliChoices()
    {
        WriteToday("""{ "agentProviders": [ { "key": "Claude", "name": "Claude Code", "runCommand": "claude --model claude-opus-5-5" } ] }""");
        var store = Store();
        Assert.That(store.Load().AgentProviders.Single().RunCommand, Is.EqualTo("claude --model claude-opus-5-5"));
        store.Update(s => s.AgentProviders.Add(new AgentProvider { Key = "Kimi", Name = "Kimi", RunCommand = "kimi" }));
        var all = new AgentProviderRegistry(store).All();
        Assert.That(all.Select(p => p.Key), Does.Contain("Claude").And.Contain("Kimi"));
        Assert.That(ProviderModel.Get(all.First(p => p.Key == "Claude").RunCommand), Is.EqualTo("claude-opus-5-5"), "the model shown and used is the --model in the run command");
    }

    [Test]
    public void Setting_Projects_ImportRoundTripAndDeepLinkToEachProject()
    {
        WriteToday("""{ "projects": [ { "name": "Alpha", "path": "D:\\a" }, { "name": "Beta", "path": "D:\\b", "tabAlias": "B" } ] }""");
        var store = Store();
        var settings = store.Load();
        Assert.That(ProjectRoster.FindByName(settings, "Beta")!.TabAlias, Is.EqualTo("B"));
        store.Update(s => ProjectRoster.FindByName(s, "Beta")!.TabColor = "#ff8800");
        Assert.That(ProjectRoster.FindByName(store.Load(), "Beta")!.TabColor, Is.EqualTo("#ff8800"));
        Assert.That(ProjectSetupMenu.SettingFor(store, "Beta"), Is.EqualTo("MindAttic:Settings:MindAttic.Launcher:projects:1:tabAlias"));
        Assert.That(ProjectSetupMenu.SettingFor(store, "Gone"), Is.Null);
    }

    [Test]
    public void Setting_DiscoveryIgnore_ImportsRoundTripsAndHidesRepoFromDiscovery()
    {
        var root = Path.Combine(dir, "ws");
        var repo = Path.Combine(root, "NewRepo");
        Directory.CreateDirectory(Path.Combine(repo, ".git"));
        WriteToday("{}");
        var store = Store();
        Assert.That(ProjectDiscovery.FindUnregistered(store.Load(), root).Select(r => r.Path), Does.Contain(repo));
        store.Update(s => (s.DiscoveryIgnore ??= []).Add(repo));
        Assert.That(ProjectDiscovery.FindUnregistered(store.Load(), root).Select(r => r.Path), Does.Not.Contain(repo));
    }

    [Test]
    public void EverySettingHasItsOwnTest()
    {
        var tested = GetType().GetMethods().Select(m => m.Name).Where(n => n.StartsWith("Setting_")).ToList();
        foreach (var field in SettingsManifestBuilder.Build<AppSettings>().Fields)
            Assert.That(tested.Any(t => t.StartsWith("Setting_" + char.ToUpperInvariant(field.Property[0]) + field.Property[1..] + "_")), Is.True, field.Property);
    }

    [Test]
    public void ManifestShowsCollectionsWithTheirFieldsAndHidesExtensionData()
    {
        var m = SettingsManifestBuilder.Build<AppSettings>();
        Assert.That(m.Fields.Select(f => f.Property), Is.EquivalentTo(new[] { "windowsTerminalSettingsPath", "agentProviders", "projects", "discoveryIgnore" }));
        var projects = m.Fields.Single(f => f.Property == "projects");
        Assert.That(projects.Kind, Is.EqualTo(SettingKind.Collection));
        Assert.That(projects.Fields!.Select(f => f.Property), Does.Contain("tabAlias").And.Contain("colorScheme").And.Not.Contain("extra"));
        Assert.That(m.Credentials.Select(c => c.ProviderId), Is.EquivalentTo(new[] { "claude", "gemini", "grok", "kimi" }));
    }

    [Test]
    public void UnknownKeysStillRoundTrip()
    {
        WriteToday("""{ "mobile": { "enabled": true }, "projects": [] }""");
        var store = Store();
        store.Save(store.Load());
        Assert.That(File.ReadAllText(Path.Combine(dir, "vault", "settings.json")), Does.Contain("\"mobile\""));
    }

    [Test]
    public void LegacyWorkspaceSettingsStillSeedAFreshInstall()
    {
        var legacy = Path.Combine(dir, "legacy-settings.json");
        File.WriteAllText(legacy, """{ "WindowsTerminalSettingsPath": "C:\\wt.json" }""");
        var settings = Store(legacy).Load();
        Assert.That(settings.WindowsTerminalSettingsPath, Is.EqualTo(@"C:\wt.json"));
        Assert.That(File.Exists(Path.Combine(dir, "vault", SettingsManifest.FileName)), Is.True);
    }

    [Test]
    public void AgentCliKeys_ComeFromTheLaunchersAssignedNamedKey()
    {
        var keys = MindAttic.Vault.Credentials.NamedKeyStore.ForRoamingRoot();
        keys.Add("gemini", "shared", "g-shared");
        keys.Add("gemini", "launcher", "g-launcher");
        keys.Assignments.Set(SettingsStore.AppBucket, "gemini", new[] { "launcher" });
        var psi = new System.Diagnostics.ProcessStartInfo("x");
        ProviderCredentials.Apply(psi, "Gemini");
        Assert.That(psi.Environment.Values, Does.Contain("g-launcher"));
        Assert.That(psi.Environment.Values, Does.Not.Contain("g-shared"));
    }

    [Test]
    public void SettingsMenuOpensVaultAtTheLaunchersTab()
    {
        string? opened = null;
        Assert.That(SettingsMenu.Open((app, setting) => { opened = app + "|" + setting; return true; }), Is.True);
        Assert.That(opened, Is.EqualTo("MindAttic.Launcher|"));
    }

    [Test]
    public void RunPs1_WritesTheSettingsManifestAfterPublishing()
    {
        // LAMP step 7 (M.LNCH-T102): a redeploy refreshes the manifest MindAttic Vault renders, not only the app's next start.
        var root = TestContext.CurrentContext.TestDirectory;
        while (!System.IO.Directory.Exists(System.IO.Path.Combine(root, ".git")) && !System.IO.File.Exists(System.IO.Path.Combine(root, ".git")))
            root = System.IO.Path.GetDirectoryName(root) ?? throw new InvalidOperationException("no repo root above the tests");
        Assert.That(System.IO.File.ReadAllText(System.IO.Path.Combine(root, "run.ps1")), Does.Contain("-ArgumentList '--write-settings-manifest'"));
    }
}
