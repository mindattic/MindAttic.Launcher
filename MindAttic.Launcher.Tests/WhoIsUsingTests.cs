using MindAttic.Launcher.Services;
using NUnit.Framework;

namespace MindAttic.Launcher.Tests;

/// <summary>Who is using this Launcher is resolved once at startup (M.LNCH-T71), from the org's current, active
/// members (<see cref="OrchestratorMembers"/>) rather than per Claude tab — the screen this replaced, M.LNCH-T65.</summary>
[TestFixture]
public sealed class WhoIsUsingTests
{
    [TearDown]
    public void TearDown()
    {
        OrchestratorMembers.OrchestratorOverride = null;
        WhoIsUsing.AskOverride = null;
        WhoIsUsing.RosterOverride = null;
        WhoIsUsing.SwitchToApiKeyOverride = null;
    }

    // ---- OrchestratorMembers ----------------------------------------------------------

    [Test]
    public void ActiveRoster_is_null_when_the_orchestrator_has_nothing_to_say()
    {
        OrchestratorMembers.OrchestratorOverride = _ => null;

        Assert.That(OrchestratorMembers.ActiveRoster(), Is.Null);
    }

    [Test]
    public void ActiveRoster_is_null_when_no_member_is_active()
    {
        OrchestratorMembers.OrchestratorOverride = _ => """[{"Email":"pending@mindattic.com","Name":"Pat","Role":"member","Tier":"standard","Status":"pending"}]""";

        Assert.That(OrchestratorMembers.ActiveRoster(), Is.Null);
    }

    [Test]
    public void ActiveRoster_keeps_only_active_members_sorted_by_name_then_email()
    {
        OrchestratorMembers.OrchestratorOverride = _ => """
            [
                {"Email":"z@mindattic.com","Name":"Amy","Role":"member","Tier":"standard","Status":"active"},
                {"Email":"a@mindattic.com","Name":null,"Role":"admin","Tier":"premium","Status":"active"},
                {"Email":"pending@mindattic.com","Name":"Pat","Role":"member","Tier":"standard","Status":"pending"}
            ]
            """;

        var roster = OrchestratorMembers.ActiveRoster();

        Assert.That(roster!.Select(m => m.Email), Is.EqualTo(new[] { "a@mindattic.com", "z@mindattic.com" }));
    }

    [Test]
    public void Describe_falls_back_to_the_email_when_no_name_is_set()
    {
        var member = new MemberOption("a@mindattic.com", null, "admin", "premium", "active");

        Assert.That(member.Describe(), Is.EqualTo("a@mindattic.com — admin, premium"));
    }

    [Test]
    public void Describe_omits_the_role_and_tier_when_neither_is_set()
    {
        var member = new MemberOption(WhoIsUsing.ApiKeyOption, "API Key", "", "", "");

        Assert.That(member.Describe(), Is.EqualTo("API Key"));
    }

    // ---- WhoIsUsing.Resolve -------------------------------------------------------------

    [Test]
    public void Resolve_asks_nothing_when_there_is_no_roster()
    {
        WhoIsUsing.RosterOverride = () => null;
        WhoIsUsing.AskOverride = _ => throw new AssertionException("asked with no roster");

        Assert.That(WhoIsUsing.Resolve(), Is.Null);
    }

    [Test]
    public void Resolve_asks_nothing_with_exactly_one_active_member()
    {
        var jordan = new MemberOption("jordan@mindattic.com", "Jordan", "member", "standard", "active");
        WhoIsUsing.RosterOverride = () => new[] { jordan };
        WhoIsUsing.AskOverride = _ => throw new AssertionException("asked with one eligible member");

        Assert.That(WhoIsUsing.Resolve(), Is.EqualTo("jordan@mindattic.com"));
    }

    [Test]
    public void Resolve_asks_once_and_returns_the_chosen_email_with_more_than_one_member()
    {
        var jordan = new MemberOption("jordan@mindattic.com", "Jordan", "member", "standard", "active");
        var amy = new MemberOption("amy@mindattic.com", "Amy", "admin", "premium", "active");
        IReadOnlyList<MemberOption>? asked = null;
        WhoIsUsing.RosterOverride = () => new[] { jordan, amy };
        WhoIsUsing.AskOverride = roster => { asked = roster; return amy; };

        Assert.That(WhoIsUsing.Resolve(), Is.EqualTo("amy@mindattic.com"));
        Assert.That(asked!.Select(m => m.Email), Is.EqualTo(new[] { "jordan@mindattic.com", "amy@mindattic.com", WhoIsUsing.ApiKeyOption }));
    }

    [Test]
    public void Resolve_returns_null_when_the_question_is_declined()
    {
        WhoIsUsing.RosterOverride = () => new[]
        {
            new MemberOption("jordan@mindattic.com", "Jordan", "member", "standard", "active"),
            new MemberOption("amy@mindattic.com", "Amy", "admin", "premium", "active"),
        };
        WhoIsUsing.AskOverride = _ => null;

        Assert.That(WhoIsUsing.Resolve(), Is.Null);
    }

    [Test]
    public void Resolve_offers_the_API_Key_alongside_the_real_roster()
    {
        WhoIsUsing.RosterOverride = () => new[]
        {
            new MemberOption("jordan@mindattic.com", "Jordan", "member", "standard", "active"),
            new MemberOption("amy@mindattic.com", "Amy", "admin", "premium", "active"),
        };
        IReadOnlyList<MemberOption>? asked = null;
        WhoIsUsing.AskOverride = roster => { asked = roster; return null; };

        WhoIsUsing.Resolve();

        var apiKey = asked!.Last();
        Assert.That(apiKey.Email, Is.EqualTo(WhoIsUsing.ApiKeyOption));
        Assert.That(apiKey.Describe(), Is.EqualTo("API Key — bill every CLI to the organization's Vault key, not a person's seat"));
    }

    [Test]
    public void Resolve_switches_billing_to_the_API_key_and_returns_null_when_it_is_chosen()
    {
        WhoIsUsing.RosterOverride = () => new[]
        {
            new MemberOption("jordan@mindattic.com", "Jordan", "member", "standard", "active"),
            new MemberOption("amy@mindattic.com", "Amy", "admin", "premium", "active"),
        };
        WhoIsUsing.AskOverride = roster => roster.Single(m => m.Email == WhoIsUsing.ApiKeyOption);
        var switched = false;
        WhoIsUsing.SwitchToApiKeyOverride = () => switched = true;

        Assert.That(WhoIsUsing.Resolve(), Is.Null);
        Assert.That(switched, Is.True);
    }

    [Test]
    public void Resolve_does_not_switch_billing_when_a_real_member_is_chosen()
    {
        var amy = new MemberOption("amy@mindattic.com", "Amy", "admin", "premium", "active");
        WhoIsUsing.RosterOverride = () => new[]
        {
            new MemberOption("jordan@mindattic.com", "Jordan", "member", "standard", "active"), amy,
        };
        WhoIsUsing.AskOverride = _ => amy;
        var switched = false;
        WhoIsUsing.SwitchToApiKeyOverride = () => switched = true;

        Assert.That(WhoIsUsing.Resolve(), Is.EqualTo("amy@mindattic.com"));
        Assert.That(switched, Is.False);
    }

    // ---- WhoIsUsing.Prompt ---------------------------------------------------------------

    [Test]
    public void Prompt_returns_the_typed_choice()
    {
        var roster = new[]
        {
            new MemberOption("jordan@mindattic.com", "Jordan", "member", "standard", "active"),
            new MemberOption("amy@mindattic.com", "Amy", "admin", "premium", "active"),
        };

        var chosen = WhoIsUsing.Prompt.Ask(roster, new StringReader("2\n"), TextWriter.Null);

        Assert.That(chosen, Is.EqualTo(roster[1]));
    }

    [Test]
    public void Prompt_returns_null_on_a_blank_or_out_of_range_line()
    {
        var roster = new[] { new MemberOption("jordan@mindattic.com", "Jordan", "member", "standard", "active") };

        Assert.That(WhoIsUsing.Prompt.Ask(roster, new StringReader("\n"), TextWriter.Null), Is.Null);
        Assert.That(WhoIsUsing.Prompt.Ask(roster, new StringReader("9\n"), TextWriter.Null), Is.Null);
    }

    [Test]
    public void Prompt_lists_every_member_by_number()
    {
        var roster = new[]
        {
            new MemberOption("jordan@mindattic.com", "Jordan", "member", "standard", "active"),
            new MemberOption("amy@mindattic.com", "Amy", "admin", "premium", "active"),
        };
        var output = new StringWriter();

        WhoIsUsing.Prompt.Ask(roster, new StringReader("\n"), output);

        var text = output.ToString();
        Assert.That(text, Does.Contain("1) Jordan — member, standard"));
        Assert.That(text, Does.Contain("2) Amy — admin, premium"));
    }
}
