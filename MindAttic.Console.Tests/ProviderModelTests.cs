using MindAttic.Console.Services;
using NUnit.Framework;

namespace MindAttic.Console.Tests;

[TestFixture]
public sealed class ProviderModelTests
{
    [Test]
    public void Get_returns_model_after_long_flag()
    {
        Assert.That(ProviderModel.Get("claude --dangerously-skip-permissions --model claude-sonnet-4-6"),
            Is.EqualTo("claude-sonnet-4-6"));
    }

    [Test]
    public void Get_returns_model_after_short_flag()
    {
        Assert.That(ProviderModel.Get("codex -m gpt-x"), Is.EqualTo("gpt-x"));
    }

    [Test]
    public void Get_returns_null_when_no_model_flag()
    {
        Assert.That(ProviderModel.Get("codex --dangerously-bypass-approvals-and-sandbox"), Is.Null);
    }

    [Test]
    public void Get_returns_null_for_blank()
    {
        Assert.That(ProviderModel.Get(""), Is.Null);
        Assert.That(ProviderModel.Get(null), Is.Null);
    }

    [Test]
    public void Set_rewrites_existing_flag_in_place()
    {
        Assert.That(
            ProviderModel.Set("claude --dangerously-skip-permissions --model claude-opus-4-8", "claude-sonnet-4-6"),
            Is.EqualTo("claude --dangerously-skip-permissions --model claude-sonnet-4-6"));
    }

    [Test]
    public void Set_preserves_trailing_args_when_rewriting()
    {
        Assert.That(ProviderModel.Set("claude --model old --foo bar", "new"),
            Is.EqualTo("claude --model new --foo bar"));
    }

    [Test]
    public void Set_appends_flag_when_absent()
    {
        Assert.That(ProviderModel.Set("codex --dangerously-bypass-approvals-and-sandbox", "gpt-x"),
            Is.EqualTo("codex --dangerously-bypass-approvals-and-sandbox --model gpt-x"));
    }

    [Test]
    public void Set_blank_removes_flag_without_leaving_double_space()
    {
        Assert.That(ProviderModel.Set("claude --dangerously-skip-permissions --model claude-opus-4-8", ""),
            Is.EqualTo("claude --dangerously-skip-permissions"));
    }

    [Test]
    public void Set_blank_removes_mid_command_flag()
    {
        Assert.That(ProviderModel.Set("claude --model old --foo", "  "),
            Is.EqualTo("claude --foo"));
    }

    [Test]
    public void Set_trims_the_model_value()
    {
        Assert.That(ProviderModel.Set("claude", "  claude-sonnet-4-6  "),
            Is.EqualTo("claude --model claude-sonnet-4-6"));
    }

    [Test]
    public void Set_then_Get_round_trips()
    {
        var updated = ProviderModel.Set("codex --dangerously-bypass-approvals-and-sandbox", "gpt-x");
        Assert.That(ProviderModel.Get(updated), Is.EqualTo("gpt-x"));
    }
}
