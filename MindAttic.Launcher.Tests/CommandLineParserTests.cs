using MindAttic.Launcher.Interop;
using NUnit.Framework;

namespace MindAttic.Launcher.Tests;

[TestFixture]
public sealed class CommandLineParserTests
{
    [Test]
    public void Empty_input_returns_empty_array()
    {
        Assert.That(CommandLineParser.Split(""), Is.Empty);
        Assert.That(CommandLineParser.Split("   "), Is.Empty);
    }

    [Test]
    public void Splits_unquoted_args()
    {
        var args = CommandLineParser.Split("claude --dangerously-skip-permissions");
        Assert.That(args, Is.EqualTo(new[] { "claude", "--dangerously-skip-permissions" }));
    }

    [Test]
    public void Preserves_quoted_arg_with_spaces()
    {
        var args = CommandLineParser.Split("claude --note \"hello world\"");
        Assert.That(args, Is.EqualTo(new[] { "claude", "--note", "hello world" }));
    }

    [Test]
    public void Preserves_escaped_quote_inside_quoted_arg()
    {
        var args = CommandLineParser.Split("tool \"escaped \\\" quote\"");
        Assert.That(args, Is.EqualTo(new[] { "tool", "escaped \" quote" }));
    }
}
