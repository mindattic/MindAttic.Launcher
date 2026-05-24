using MindAttic.Terminal.Models;
using MindAttic.Terminal.Services;
using MindAttic.Vault.Credentials;
using MindAttic.Vault.Settings;
using NUnit.Framework;

namespace MindAttic.Terminal.Tests;

[TestFixture]
public sealed class MobileBridgeTests
{
    private const string FakeHostExe = @"D:\fake\AgentHost.exe";
    private string tempRoot = "";

    [SetUp]
    public void SetUp()
    {
        tempRoot = Path.Combine(Path.GetTempPath(), "MindAttic.Terminal.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(tempRoot, recursive: true); } catch { }
    }

    private SettingsStore MakeStore(string? token)
    {
        var json = new JsonSettingsStore<AppSettings>(Path.Combine(tempRoot, "settings"));
        var tokens = new TokenStore(Path.Combine(tempRoot, "tokens"));
        var store = new SettingsStore(json, tokens);
        if (token is not null) store.SetMobileToken(token);
        return store;
    }

    private static MobileBridge MakeBridge(bool hostExePresent = true) =>
        new([FakeHostExe], _ => hostExePresent);

    private static AppSettings EnabledSettings(bool allProjects = true) => new()
    {
        Mobile = new MobileBridgeSettings
        {
            Enabled = true,
            AllProjects = allProjects,
            ServerUrl = "http://127.0.0.1:7780"
        }
    };

    [Test]
    public void FeatureEnabled_is_currently_off()
    {
        Assert.That(MobileBridge.FeatureEnabled, Is.False,
            "Kill switch must remain false until MindAttic.Mobile ships. " +
            "Flip MobileBridge.FeatureEnabled and update this test together.");
    }

    [Test]
    public void ShouldUse_returns_false_while_kill_switch_is_off_even_with_everything_else_set()
    {
        var bridge = MakeBridge();
        var settings = EnabledSettings();
        var project = new Project { Name = "Alpha", Path = "C:\\a", MobileEnabled = true };
        var store = MakeStore(token: "tok");

        Assert.That(bridge.ShouldUse(settings, project, store, out var ctx), Is.False);
        Assert.That(ctx, Is.Null);
    }

    [Test]
    public void MobileBridgeContext_does_not_carry_the_token()
    {
        // Token is never on the context type. This test pins the shape so a
        // future refactor can't reintroduce it accidentally.
        var props = typeof(MobileBridgeContext).GetProperties().Select(p => p.Name).ToArray();
        Assert.That(props, Does.Not.Contain("Token"));
    }

    [Test]
    public void Gate_logic_is_consistent_when_FeatureEnabled_would_be_on()
    {
        if (!MobileBridge.FeatureEnabled)
            Assert.Ignore("MobileBridge.FeatureEnabled is off — gate tests are dormant.");

        var bridge = MakeBridge();
        var project = new Project { Name = "Alpha", Path = "C:\\a" };
        var withToken = MakeStore(token: "tok");
        var withoutToken = MakeStore(token: null);

        Assert.That(bridge.ShouldUse(EnabledSettings(allProjects: false), project, withToken, out _), Is.False,
            "no AllProjects + no per-project opt-in → off");

        project.MobileEnabled = true;
        Assert.That(bridge.ShouldUse(EnabledSettings(allProjects: false), project, withToken, out _), Is.True,
            "per-project opt-in turns it on");

        project.MobileEnabled = false;
        Assert.That(bridge.ShouldUse(EnabledSettings(allProjects: true), project, withToken, out _), Is.False,
            "per-project hard opt-out beats AllProjects");

        project.MobileEnabled = true;
        Assert.That(bridge.ShouldUse(EnabledSettings(), project, withoutToken, out _), Is.False,
            "missing token → off");

        Assert.That(MakeBridge(hostExePresent: false).ShouldUse(EnabledSettings(), new Project { Name = "X", Path = "" }, withToken, out _), Is.False,
            "missing AgentHost.exe → off");
    }
}
