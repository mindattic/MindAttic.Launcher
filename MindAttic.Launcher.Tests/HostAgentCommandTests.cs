using MindAttic.Launcher.Commands;
using MindAttic.Launcher.Interop;
using MindAttic.Launcher.Models;
using MindAttic.Launcher.Services;
using MindAttic.Vault.Settings;
using NUnit.Framework;

namespace MindAttic.Launcher.Tests;

[TestFixture]
public sealed class HostAgentCommandTests
{
    private string tempRoot = "";

    [SetUp]
    public void SetUp()
    {
        tempRoot = Path.Combine(Path.GetTempPath(), "MindAttic.Launcher.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(tempRoot, true); } catch (IOException) { }
    }

    private AgentProviderRegistry Registry(params AgentProvider[] providers)
    {
        var store = new SettingsStore(new JsonSettingsStore<AppSettings>(Path.Combine(tempRoot, "settings")));
        var s = new AppSettings();
        foreach (var p in providers) s.AgentProviders.Add(p);
        store.Save(s);
        return new AgentProviderRegistry(store);
    }

    private static Project Proj(string path) => new() { Name = "Demo", Path = path };

    [Test]
    public void Path_wins_over_name()
    {
        var r = HostAgentCommand.ResolveRoot(@"C:\x", "Demo", _ => Proj(@"C:\other"));
        Assert.Multiple(() =>
        {
            Assert.That(r.Error, Is.Null);
            Assert.That(r.WorkingDir, Is.EqualTo(@"C:\x"));
            Assert.That(r.Project, Is.Null);
        });
    }

    [Test]
    public void Known_name_roots_at_the_project()
    {
        var r = HostAgentCommand.ResolveRoot(null, "Demo", _ => Proj(@"C:\other"));
        Assert.Multiple(() =>
        {
            Assert.That(r.WorkingDir, Is.EqualTo(@"C:\other"));
            Assert.That(r.Project, Is.Not.Null);
        });
    }

    [Test]
    public void Unknown_name_returns_1()
    {
        var r = HostAgentCommand.ResolveRoot(null, "Nope", _ => null);
        Assert.Multiple(() =>
        {
            Assert.That(r.ExitCode, Is.EqualTo(1));
            Assert.That(r.Error, Does.Contain("Nope"));
        });
    }

    [Test]
    public void Neither_name_nor_path_returns_64()
    {
        Assert.That(HostAgentCommand.ResolveRoot(" ", "", _ => null).ExitCode, Is.EqualTo(64));
    }

    [Test]
    public void Unknown_provider_returns_4_instead_of_falling_back()
    {
        var reg = Registry(new AgentProvider { Key = "Claude", Name = "C", RunCommand = "claude" });
        var r = HostAgentCommand.ResolveProvider("Bogus", reg);
        Assert.Multiple(() =>
        {
            Assert.That(r.ExitCode, Is.EqualTo(4));
            Assert.That(r.Provider, Is.Null);
        });
    }

    [Test]
    public void No_provider_resolves_to_the_first_listed_and_a_known_key_to_itself()
    {
        var reg = Registry(
            new AgentProvider { Key = "Claude", Name = "C", RunCommand = "claude" },
            new AgentProvider { Key = "Codex", Name = "X", RunCommand = "codex" });
        Assert.Multiple(() =>
        {
            Assert.That(HostAgentCommand.ResolveProvider(null, reg).Provider!.Key, Is.EqualTo("Claude"));
            Assert.That(HostAgentCommand.ResolveProvider("codex", reg).Provider!.Key, Is.EqualTo("Codex"));
        });
    }

    [Test]
    public void Blank_RunCommand_returns_2()
    {
        var r = HostAgentCommand.ResolveCommand(new AgentProvider { Key = "X", Name = "X", RunCommand = "   " });
        Assert.Multiple(() =>
        {
            Assert.That(r.ExitCode, Is.EqualTo(2));
            Assert.That(r.Parts, Is.Null);
            Assert.That(r.Error, Does.Contain("empty RunCommand"));
        });
    }

    [Test]
    public void Title_falls_back_from_title_to_name_to_the_path_folder()
    {
        var sep = Path.DirectorySeparatorChar;
        Assert.Multiple(() =>
        {
            Assert.That(HostAgentCommand.ResolveTitle("T", "N", "x"), Is.EqualTo("T"));
            Assert.That(HostAgentCommand.ResolveTitle(null, "N", "x"), Is.EqualTo("N"));
            Assert.That(HostAgentCommand.ResolveTitle(" ", "", $"C:{sep}a{sep}Folder"), Is.EqualTo("Folder"));
            Assert.That(HostAgentCommand.ResolveTitle(null, null, $"C:{sep}a{sep}Folder{sep}"), Is.EqualTo("Folder"));
        });
    }

    [Test]
    public void StartInfo_strips_NO_COLOR_and_appends_the_prompt_last()
    {
        var prior = Environment.GetEnvironmentVariable("NO_COLOR");
        Environment.SetEnvironmentVariable("NO_COLOR", "1");
        try
        {
            var psi = HostAgentCommand.BuildBaseStartInfo(["dotnet", "--flag"], tempRoot, null, "/status");
            Assert.Multiple(() =>
            {
                Assert.That(psi.EnvironmentVariables.ContainsKey("NO_COLOR"), Is.False);
                Assert.That(psi.ArgumentList, Is.EqualTo(new[] { "--flag", "/status" }));
                Assert.That(psi.WorkingDirectory, Is.EqualTo(tempRoot));
            });
        }
        finally { Environment.SetEnvironmentVariable("NO_COLOR", prior); }
    }

    [Test]
    public void StartInfo_resume_replaces_the_prompt_and_a_blank_prompt_adds_nothing()
    {
        var resumed = HostAgentCommand.BuildBaseStartInfo(["dotnet"], tempRoot, "abc", "/status");
        var blank = HostAgentCommand.BuildBaseStartInfo(["dotnet", "-x"], tempRoot, null, "  ");
        Assert.Multiple(() =>
        {
            Assert.That(resumed.ArgumentList, Is.EqualTo(new[] { "--resume", "abc" }));
            Assert.That(blank.ArgumentList, Is.EqualTo(new[] { "-x" }));
        });
    }

    private static RestartResumeRequest RelocateRequest(int pid = 4242) =>
        new(null, "11111111-2222-3333-4444-555555555555", pid, @"D:\Projects\Prose", Relocate: true);

    private void State(string session, string state) =>
        File.WriteAllText(Path.Combine(tempRoot, session + ".state.json"), $$"""{"state": "{{state}}"}""");

    [Test]
    public void No_pending_request_waits() =>
        Assert.That(HostAgentCommand.NextAction(null, 4242), Is.EqualTo(HostAgentCommand.WatchAction.Wait));

    [Test]
    public void A_relocate_request_waits_until_idle()
    {
        State(RelocateRequest().Session, "busy");

        Assert.That(HostAgentCommand.NextAction(RelocateRequest(), 4242, sessionContextDirectory: tempRoot),
            Is.EqualTo(HostAgentCommand.WatchAction.Wait));
    }

    [Test]
    public void An_idle_relocate_request_for_this_CLI_relocates_instead_of_relaunching()
    {
        State(RelocateRequest().Session, "idle");

        Assert.That(HostAgentCommand.NextAction(RelocateRequest(), 4242, sessionContextDirectory: tempRoot),
            Is.EqualTo(HostAgentCommand.WatchAction.Relocate));
    }

    [Test]
    public void A_relocate_request_for_another_CLI_is_dropped()
    {
        State(RelocateRequest().Session, "idle");

        Assert.That(HostAgentCommand.NextAction(RelocateRequest(pid: 4242), 5555, sessionContextDirectory: tempRoot),
            Is.EqualTo(HostAgentCommand.WatchAction.Drop));
    }

    [Test]
    public void An_idle_ordinary_restart_request_relaunches()
    {
        var request = new RestartResumeRequest("api", "11111111-2222-3333-4444-555555555555", 4242, @"D:\Projects\Prose");
        State(request.Session, "idle");
        var billing = Path.Combine(tempRoot, "billing.json");
        File.WriteAllText(billing, """{"mode": "api"}""");

        Assert.That(HostAgentCommand.NextAction(request, 4242, billingModePath: billing, sessionContextDirectory: tempRoot),
            Is.EqualTo(HostAgentCommand.WatchAction.Relaunch));
    }

    [Test]
    public void Pull_skips_a_non_repo_without_pulling_or_printing()
    {
        var pulled = false;
        var lines = new List<string>();
        HostAgentCommand.PullBeforeLaunch(tempRoot, "T", _ => { pulled = true; return (true, "x"); }, lines.Add, lines.Add);
        Assert.Multiple(() =>
        {
            Assert.That(pulled, Is.False);
            Assert.That(lines, Is.Empty);
        });
    }

    [Test]
    public void Pull_stays_quiet_after_the_banner_on_no_upstream_but_warns_on_real_failure()
    {
        Directory.CreateDirectory(Path.Combine(tempRoot, ".git"));
        var info = new List<string>();
        var warn = new List<string>();
        const string noUpstream = "There is no tracking information for the current branch.";
        Assume.That(GitService.IsNoUpstream(noUpstream), Is.True);

        HostAgentCommand.PullBeforeLaunch(tempRoot, "T", _ => (false, noUpstream), info.Add, warn.Add);
        Assert.Multiple(() =>
        {
            Assert.That(info, Is.EqualTo(new[] { "Pulling the latest T..." }));
            Assert.That(warn, Is.Empty);
        });

        HostAgentCommand.PullBeforeLaunch(tempRoot, "T", _ => (false, "boom\nline2"), info.Add, warn.Add);
        Assert.That(warn, Has.Count.EqualTo(3));
    }
}
