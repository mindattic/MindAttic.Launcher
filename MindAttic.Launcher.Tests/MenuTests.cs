using MindAttic.Launcher.Ui;
using NUnit.Framework;

namespace MindAttic.Launcher.Tests;

[TestFixture]
public class MenuTests
{
    private static MenuItem Item(string name, object? tag = null) => new() { Name = name, Tag = tag };

    private static List<MenuItem> Rows(bool back)
    {
        var rows = new List<MenuItem> { Item("a"), Item("b"), Item("c") };
        if (back) rows.Add(Item("< Back", MenuSentinel.Back));
        return rows;
    }

    [TestCase(ConsoleKey.UpArrow)]
    [TestCase(ConsoleKey.K)]
    public void Navigate_Up_WrapsFromZeroToLast(ConsoleKey key)
    {
        var index = 0;
        Assert.That(Menu.Navigate(key, 5, 3, ref index), Is.True);
        Assert.That(index, Is.EqualTo(4));
        Assert.That(Menu.Navigate(key, 5, 3, ref index), Is.True);
        Assert.That(index, Is.EqualTo(3));
    }

    [TestCase(ConsoleKey.DownArrow)]
    [TestCase(ConsoleKey.J)]
    public void Navigate_Down_WrapsToZero(ConsoleKey key)
    {
        var index = 4;
        Assert.That(Menu.Navigate(key, 5, 3, ref index), Is.True);
        Assert.That(index, Is.EqualTo(0));
        Menu.Navigate(key, 5, 3, ref index);
        Assert.That(index, Is.EqualTo(1));
    }

    [Test]
    public void Navigate_HomeAndEnd()
    {
        var index = 2;
        Assert.That(Menu.Navigate(ConsoleKey.Home, 5, 3, ref index), Is.True);
        Assert.That(index, Is.EqualTo(0));
        Assert.That(Menu.Navigate(ConsoleKey.End, 5, 3, ref index), Is.True);
        Assert.That(index, Is.EqualTo(4));
    }

    [Test]
    public void Navigate_PageKeys_ClampToEnds()
    {
        var index = 1;
        Menu.Navigate(ConsoleKey.PageUp, 10, 3, ref index);
        Assert.That(index, Is.EqualTo(0));
        index = 5;
        Menu.Navigate(ConsoleKey.PageUp, 10, 3, ref index);
        Assert.That(index, Is.EqualTo(2));
        index = 8;
        Menu.Navigate(ConsoleKey.PageDown, 10, 3, ref index);
        Assert.That(index, Is.EqualTo(9));
        index = 2;
        Menu.Navigate(ConsoleKey.PageDown, 10, 3, ref index);
        Assert.That(index, Is.EqualTo(5));
    }

    [TestCase(ConsoleKey.Enter)]
    [TestCase(ConsoleKey.Escape)]
    [TestCase(ConsoleKey.A)]
    public void Navigate_OtherKeys_ReturnFalseAndKeepIndex(ConsoleKey key)
    {
        var index = 2;
        Assert.That(Menu.Navigate(key, 5, 3, ref index), Is.False);
        Assert.That(index, Is.EqualTo(2));
    }

    [Test]
    public void Resolve_EnterOnItem_SelectsIt()
    {
        var rows = Rows(true);
        var r = Menu.Resolve(ConsoleKey.Enter, rows, 1, true, null);
        Assert.That(r!.Selected, Is.SameAs(rows[1]));
        Assert.That(r.Back, Is.False);
        Assert.That(r.Index, Is.EqualTo(1));
    }

    [Test]
    public void Resolve_EnterOnBackRow_YieldsBack()
    {
        var rows = Rows(true);
        var r = Menu.Resolve(ConsoleKey.Enter, rows, 3, true, null);
        Assert.That(r!.Back, Is.True);
        Assert.That(r.Selected, Is.Null);
    }

    [Test]
    public void Resolve_Escape_BackOnlyWhenAllowed()
    {
        Assert.That(Menu.Resolve(ConsoleKey.Escape, Rows(true), 0, true, null)!.Back, Is.True);
        Assert.That(Menu.Resolve(ConsoleKey.Escape, Rows(false), 0, false, null), Is.Null);
    }

    [Test]
    public void Resolve_CustomKey_TargetsHighlightedItem()
    {
        var rows = Rows(true);
        var keys = new HashSet<ConsoleKey> { ConsoleKey.S };
        var r = Menu.Resolve(ConsoleKey.S, rows, 2, true, keys);
        Assert.That(r!.CustomKey, Is.EqualTo(ConsoleKey.S));
        Assert.That(r.KeyTarget, Is.SameAs(rows[2]));
    }

    [Test]
    public void Resolve_CustomKey_IgnoredOnBackRow()
    {
        var keys = new HashSet<ConsoleKey> { ConsoleKey.S };
        Assert.That(Menu.Resolve(ConsoleKey.S, Rows(true), 3, true, keys), Is.Null);
    }

    [Test]
    public void Resolve_UnregisteredKey_ReturnsNull()
    {
        var keys = new HashSet<ConsoleKey> { ConsoleKey.S };
        Assert.That(Menu.Resolve(ConsoleKey.X, Rows(true), 0, true, keys), Is.Null);
        Assert.That(Menu.Resolve(ConsoleKey.S, Rows(true), 0, true, null), Is.Null);
    }

    [Test]
    public void Thumb_FillsTrackWhenEverythingFits()
    {
        Assert.That(Menu.Thumb(5, 5, 0, 7), Is.EqualTo((0, 7)));
        Assert.That(Menu.Thumb(3, 10, 0, 10), Is.EqualTo((0, 10)));
    }

    [Test]
    public void Thumb_ScalesLengthAndOffset()
    {
        Assert.That(Menu.Thumb(20, 10, 0, 10), Is.EqualTo((0, 5)));
        Assert.That(Menu.Thumb(20, 10, 10, 10), Is.EqualTo((5, 5)));
        Assert.That(Menu.Thumb(20, 10, 5, 10), Is.EqualTo((2, 5))); // 2.5 rounds to even
    }

    [Test]
    public void Thumb_LengthIsAtLeastOne()
    {
        var (start, length) = Menu.Thumb(1000, 3, 997, 5);
        Assert.That(length, Is.EqualTo(1));
        Assert.That(start, Is.EqualTo(4));
    }

    [Test]
    public void Truncate_EdgeCases()
    {
        Assert.That(Menu.Truncate("hello", 0), Is.EqualTo(""));
        Assert.That(Menu.Truncate("hello", -2), Is.EqualTo(""));
        Assert.That(Menu.Truncate("hello", 1), Is.EqualTo("…"));
        Assert.That(Menu.Truncate("hello", 5), Is.EqualTo("hello"));
        Assert.That(Menu.Truncate("hello", 4), Is.EqualTo("hel…"));
        Assert.That(Menu.Truncate("hi", 10), Is.EqualTo("hi"));
    }

    [Test]
    public void Fit_KeepsMarkupWhenItFits()
    {
        Assert.That(Menu.Fit("[green]abc[/]", 3), Is.EqualTo("[green]abc[/]"));
    }

    [Test]
    public void Fit_DropsMarkupAndEscapesWhenTooWide()
    {
        Assert.That(Menu.Fit("[green]abcdef[/]", 4), Is.EqualTo("abc…"));
        Assert.That(Menu.Fit("[green]ab[[cd[/]", 4), Is.EqualTo("ab[[…"));
    }
}
