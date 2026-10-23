using System.IO;
using System.Linq;
using System.Text.Json;
using MindAttic.Launcher.Models;
using MindAttic.Launcher.Services;
using NUnit.Framework;

namespace MindAttic.Launcher.Tests;

/// <summary>Initials icons on terminal tabs (M.LNCH-T56): each Claude login gets its own Windows Terminal profile through
/// a fragment whose icon is its initials on its tab color, and its tabs open with it. Every test writes a temp folder, never
/// the real %LOCALAPPDATA% fragments folder or Terminal's settings.json.</summary>
[TestFixture]
public sealed class LoginTerminalProfilesTests
{
    private string root = "";

    private static readonly AgentProvider Claude = new() { Key = "Claude", Name = "Claude Code", RunCommand = "claude" };
    private static readonly AgentProvider Codex = new() { Key = "Codex", Name = "OpenAI Codex", RunCommand = "codex" };
    private static readonly Project Repo = new() { Name = "Repo", Path = @"D:\Projects\Repo", TabColor = "#123456" };

    private static readonly LoginLook Jordan =
        new LoginLook("Jordan-Reed", "JR", "#2E7D32") { Alias = "Jordan Reed", Email = "jordan@mindattic.com" };

    [SetUp]
    public void SetUp()
    {
        root = Path.Combine(Path.GetTempPath(), "launcher-fragments-" + Path.GetRandomFileName());
        Directory.CreateDirectory(root);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(root, recursive: true); } catch (IOException) { }
    }

    private static JsonElement Profile(string fragmentPath)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(fragmentPath));
        return doc.RootElement.GetProperty("profiles")[0].Clone();
    }

    [Test]
    public void The_default_fragments_folder_is_terminals_mindattic_one()
    {
        Assert.That(LoginTerminalProfiles.DefaultFragmentsDir,
            Does.EndWith(Path.Combine("Microsoft", "Windows Terminal", "Fragments", "MindAttic")));
        Assert.That(new LoginTerminalProfiles(root).FragmentsDir, Is.EqualTo(root));
    }

    [Test]
    public void Ensure_writes_a_fragment_whose_profile_is_named_iconed_and_colored_as_the_login()
    {
        var profiles = new LoginTerminalProfiles(root);

        var guid = profiles.Ensure(Jordan);

        Assert.That(guid, Is.EqualTo(LoginTerminalProfiles.ProfileGuid(Jordan)).And.Match(@"^\{[0-9a-f-]{36}\}$"));
        var fragment = profiles.FragmentPath(Jordan);
        Assert.That(Path.GetDirectoryName(fragment), Is.EqualTo(root));
        var profile = Profile(fragment);
        Assert.That(profile.GetProperty("guid").GetString(), Is.EqualTo(guid));
        Assert.That(profile.GetProperty("name").GetString(), Is.EqualTo("Claude - Jordan Reed"));
        Assert.That(profile.GetProperty("tabColor").GetString(), Is.EqualTo("#2E7D32"));
        var icon = profile.GetProperty("icon").GetString()!;
        Assert.That(icon, Does.StartWith(root));
        Assert.That(File.ReadAllBytes(icon).Take(8), Is.EqualTo(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }), "a PNG");
    }

    [Test]
    public void The_icon_is_the_initials_on_the_tab_color()
    {
        var png = LoginTerminalProfiles.RenderIcon("JR", "#2E7D32");

        using var bitmap = new System.Drawing.Bitmap(new MemoryStream(png));
        Assert.That((bitmap.Width, bitmap.Height), Is.EqualTo((LoginTerminalProfiles.IconSize, LoginTerminalProfiles.IconSize)));
        var edge = bitmap.GetPixel(LoginTerminalProfiles.IconSize / 2, 3);
        Assert.That((edge.R, edge.G, edge.B), Is.EqualTo(((byte)0x2E, (byte)0x7D, (byte)0x32)), "the tab color fills the square");
        var pixels = Enumerable.Range(0, bitmap.Width).SelectMany(x => Enumerable.Range(0, bitmap.Height).Select(y => bitmap.GetPixel(x, y)));
        Assert.That(pixels.Any(p => p.A == 255 && p.R > 200 && p.G > 200 && p.B > 200), "white initials on a dark color");
    }

    [Test]
    public void A_new_alias_or_initials_rewrites_the_same_profile_and_drops_the_old_icon()
    {
        var profiles = new LoginTerminalProfiles(root);
        var guid = profiles.Ensure(Jordan);
        var oldIcon = Profile(profiles.FragmentPath(Jordan)).GetProperty("icon").GetString()!;

        var renamed = new LoginLook("Jo-Ann-Reed", "JAR", "#2E7D32") { Alias = "Jo Ann Reed", Email = "jordan@mindattic.com" };
        Assert.That(profiles.Ensure(renamed), Is.EqualTo(guid), "the same login keeps its profile across a rename");

        Assert.That(Directory.GetFiles(root, "*.json"), Has.Length.EqualTo(1));
        var profile = Profile(profiles.FragmentPath(renamed));
        Assert.That(profile.GetProperty("name").GetString(), Is.EqualTo("Claude - Jo Ann Reed"));
        var newIcon = profile.GetProperty("icon").GetString()!;
        Assert.That(newIcon, Is.Not.EqualTo(oldIcon), "a redrawn icon gets a new path, which Terminal doesn't have cached");
        Assert.That(File.Exists(newIcon) && !File.Exists(oldIcon));
    }

    [Test]
    public void An_unchanged_login_leaves_its_fragment_alone()
    {
        var profiles = new LoginTerminalProfiles(root);
        profiles.Ensure(Jordan);
        var fragment = profiles.FragmentPath(Jordan);
        var stamp = new System.DateTime(2020, 1, 1, 0, 0, 0, System.DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(fragment, stamp);

        profiles.Ensure(Jordan);

        Assert.That(File.GetLastWriteTimeUtc(fragment), Is.EqualTo(stamp));
    }

    [Test]
    public void Different_logins_get_different_profiles()
    {
        var other = new LoginLook("Amy-Reed", "AR", "#1565C0") { Alias = "Amy Reed", Email = "amy@mindattic.com" };
        var profiles = new LoginTerminalProfiles(root);

        Assert.That(profiles.Ensure(other), Is.Not.EqualTo(profiles.Ensure(Jordan)));
        Assert.That(Directory.GetFiles(root, "*.json"), Has.Length.EqualTo(2));
    }

    [Test]
    public void A_claude_tab_opens_with_its_logins_profile_written_on_open()
    {
        var wt = new WindowsTerminalLauncher(claudeLook: () => Jordan, profiles: new LoginTerminalProfiles(root));

        var tab = wt.BuildAgentTab(Repo, Claude, "mindattic.exe");
        Assert.That(Directory.GetFiles(root), Is.Empty, "building a tab writes nothing");
        Assert.That(tab.Title, Does.StartWith("JR: "), "the title badge stays");

        var opened = wt.WithProfileWritten(tab);

        var args = WindowsTerminalLauncher.BuildArgList(opened).ToList();
        Assert.That(args.Take(5), Is.EqualTo(new[] { "-w", "Jordan-Reed", "new-tab", "-p", LoginTerminalProfiles.ProfileGuid(Jordan) }));
        Assert.That(File.Exists(new LoginTerminalProfiles(root).FragmentPath(Jordan)));
    }

    [Test]
    public void A_profile_that_cant_be_written_leaves_the_tab_on_terminals_default()
    {
        var blocked = Path.Combine(root, "not-a-folder");
        File.WriteAllText(blocked, "");
        var wt = new WindowsTerminalLauncher(claudeLook: () => Jordan, profiles: new LoginTerminalProfiles(blocked));

        var opened = wt.WithProfileWritten(wt.BuildAgentTabAtPath("General [Claude]", @"D:\Projects", Claude, "mindattic.exe"));

        Assert.That(opened.Profile, Is.Null);
        Assert.That(WindowsTerminalLauncher.BuildArgList(opened), Has.No.Member("-p"));
        Assert.That(opened.Window, Is.EqualTo("Jordan-Reed"));
    }

    [Test]
    public void Other_providers_and_unknown_logins_open_with_no_profile()
    {
        var wt = new WindowsTerminalLauncher(claudeLook: () => Jordan, profiles: new LoginTerminalProfiles(root));
        var unknown = new WindowsTerminalLauncher(claudeLook: () => null, profiles: new LoginTerminalProfiles(root));

        foreach (var tab in new[] { wt.BuildAgentTab(Repo, Codex, "mindattic.exe"), unknown.BuildAgentTab(Repo, Claude, "mindattic.exe") })
        {
            var opened = wt.WithProfileWritten(tab);
            Assert.That(opened.Profile, Is.Null);
            Assert.That(WindowsTerminalLauncher.BuildArgList(opened), Has.No.Member("-p"));
        }
        Assert.That(Directory.GetFiles(root), Is.Empty);
    }

    [Test]
    public void Logins_from_the_look_file_are_keyed_by_their_email()
    {
        var dir = Path.Combine(root, ".claude-accounts", "jordan@mindattic.com");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, AccountScope.LookFileName), """{ "alias": "Jordan Reed" }""");

        var look = LoginLook.ForConfigDir(dir);

        Assert.That(look.Email, Is.EqualTo("jordan@mindattic.com"));
        Assert.That(LoginTerminalProfiles.ProfileGuid(look), Is.EqualTo(LoginTerminalProfiles.ProfileGuid(Jordan)));
    }
}
