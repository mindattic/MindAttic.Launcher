using MindAttic.Launcher.Services;
using NUnit.Framework;

namespace MindAttic.Launcher.Tests;

/// <summary>
/// M.LNCH-T111: the pure rules DesktopPinWatcher acts on — the hard MindAttic-only scope filter, and whether a
/// detected MindAttic window should actually be moved to Desktop 1 right now.
/// </summary>
[TestFixture]
public sealed class DesktopPinPolicyTests
{
    private const string Root = @"D:\Projects\MindAttic";

    [TestCase(@"D:\Projects\MindAttic\MindAttic.Launcher\bin\Release\MindAttic.Launcher.exe", TestName = "DirectChild")]
    [TestCase(@"D:\Projects\MindAttic\Unreal.SpaceGod\Saved\Binaries\Win64\SpaceGod.exe", TestName = "DeepUnrealBuildOutput")]
    [TestCase(@"D:\Projects\MindAttic\Unity.SpaceGod\Builds\Win64\SpaceGod.exe", TestName = "DeepUnityBuildOutput")]
    [TestCase(@"D:\Projects\MindAttic\Unity.Rea.Chain\Library\Bee\artifacts\Chain.exe", TestName = "UnityLibraryFolder")]
    [TestCase(@"d:\projects\mindattic\MindAttic.Vault\bin\Debug\MindAttic.Vault.Desktop.Wpf.exe", TestName = "CaseInsensitive")]
    public void IsUnderMindAtticRoot_true_for_paths_under_the_workspace(string path)
    {
        Assert.That(DesktopPinPolicy.IsUnderMindAtticRoot(path, Root), Is.True);
    }

    [TestCase(@"D:\Projects\MindAtticX\evil.exe", TestName = "SimilarlyNamedSiblingFolder_notTheRoot")]
    [TestCase(@"D:\Projects\MindAttic.exe", TestName = "FileNamedLikeTheRootItself")]
    [TestCase(@"C:\Program Files\Discord\Discord.exe", TestName = "UnrelatedApp")]
    [TestCase(@"C:\Program Files\Google\Chrome\Application\chrome.exe", TestName = "Browser")]
    [TestCase(@"C:\Program Files (x86)\Steam\steamapps\common\SomeGame\Game.exe", TestName = "UnrelatedSteamGame")]
    [TestCase(Root, TestName = "ExactlyTheRootItself_noFileComponent")]
    [TestCase(null, TestName = "NullPath")]
    [TestCase("", TestName = "EmptyPath")]
    [TestCase("   ", TestName = "WhitespacePath")]
    public void IsUnderMindAtticRoot_false_for_everything_else(string? path)
    {
        Assert.That(DesktopPinPolicy.IsUnderMindAtticRoot(path, Root), Is.False);
    }

    [Test]
    public void IsUnderMindAtticRoot_false_when_root_is_blank()
    {
        Assert.That(DesktopPinPolicy.IsUnderMindAtticRoot(@"D:\Projects\MindAttic\x.exe", null), Is.False);
        Assert.That(DesktopPinPolicy.IsUnderMindAtticRoot(@"D:\Projects\MindAttic\x.exe", ""), Is.False);
    }

    [Test]
    public void IsUnderMindAtticRoot_false_for_an_unresolvable_path()
    {
        // Embedded NUL and other invalid path characters make Path.GetFullPath throw; that must mean "no",
        // never bubble up and crash the watcher.
        Assert.That(DesktopPinPolicy.IsUnderMindAtticRoot("D:\\Projects\\MindAttic\\\0bad.exe", Root), Is.False);
    }

    [TestCase(true, 2, 1, TestName = "SettingOn_MultipleDesktops_NotOnDesktop1")]
    [TestCase(true, 5, 4, TestName = "SettingOn_ManyDesktops_FarFromDesktop1")]
    public void ShouldPin_true_when_everything_lines_up(bool settingOn, int count, int current)
    {
        Assert.That(DesktopPinPolicy.ShouldPin(settingOn, count, current), Is.True);
    }

    [Test]
    public void ShouldPin_false_when_the_setting_is_off()
    {
        Assert.That(DesktopPinPolicy.ShouldPin(settingOn: false, desktopCount: 3, currentDesktopNumber: 1), Is.False);
    }

    [TestCase(0, TestName = "ZeroDesktops_shouldNeverHappenButIsNotA_crash")]
    [TestCase(1, TestName = "OnlyOneDesktop_nothingToDo")]
    public void ShouldPin_false_when_one_or_no_desktops_exist(int count)
    {
        Assert.That(DesktopPinPolicy.ShouldPin(settingOn: true, desktopCount: count, currentDesktopNumber: 1), Is.False);
    }

    [Test]
    public void ShouldPin_false_when_the_desktop_count_is_unknown()
    {
        Assert.That(DesktopPinPolicy.ShouldPin(settingOn: true, desktopCount: null, currentDesktopNumber: 1), Is.False);
    }

    [Test]
    public void ShouldPin_false_when_the_windows_current_desktop_is_unknown()
    {
        Assert.That(DesktopPinPolicy.ShouldPin(settingOn: true, desktopCount: 3, currentDesktopNumber: null), Is.False);
    }

    [Test]
    public void ShouldPin_false_when_the_window_is_already_on_desktop_1()
    {
        Assert.That(DesktopPinPolicy.ShouldPin(settingOn: true, desktopCount: 3, currentDesktopNumber: 0), Is.False);
    }

    [Test]
    public void ShouldPin_respects_a_different_target_desktop_number()
    {
        Assert.That(DesktopPinPolicy.ShouldPin(settingOn: true, desktopCount: 3, currentDesktopNumber: 2, targetDesktopNumber: 2), Is.False);
        Assert.That(DesktopPinPolicy.ShouldPin(settingOn: true, desktopCount: 3, currentDesktopNumber: 0, targetDesktopNumber: 2), Is.True);
    }
}
