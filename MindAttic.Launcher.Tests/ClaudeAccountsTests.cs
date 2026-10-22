using MindAttic.Launcher.Services;
using NUnit.Framework;

namespace MindAttic.Launcher.Tests;

/// <summary>Which Claude login a tab's CLI starts on (M.LNCH-T53): the account catalog
/// (<see cref="ClaudeAccountCatalog"/>), the busy marker (<see cref="ClaudeLoginMarker"/>), the resolution
/// engine (<see cref="ClaudeAccountResolver"/>), and the interactive prompt (<see cref="ClaudeAccountPrompt"/>).</summary>
[TestFixture]
public sealed class ClaudeAccountsTests
{
    private string accountsRoot = "";

    [SetUp]
    public void SetUp()
    {
        accountsRoot = Path.Combine(Path.GetTempPath(), "MindAttic.Launcher.Tests", "accounts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(accountsRoot);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(accountsRoot, recursive: true); } catch (IOException) { }
        ClaudeAccountCatalog.OrchestratorOverride = null;
        ClaudeAccountCatalog.GlobalEmailOverride = null;
        ClaudeAccountCatalog.BusyOverride = null;
    }

    private string Folder(string email)
    {
        var dir = Path.Combine(accountsRoot, email);
        Directory.CreateDirectory(dir);
        return dir;
    }

    // ---- ClaudeAccountCatalog -------------------------------------------------

    [Test]
    public void PerCliLogins_is_empty_when_the_folder_does_not_exist()
    {
        Assert.That(ClaudeAccountCatalog.PerCliLogins(Path.Combine(accountsRoot, "nope")), Is.Empty);
    }

    [Test]
    public void PerCliLogins_lists_each_subfolder_by_email_sorted()
    {
        Folder("b@mindattic.com");
        Folder("a@mindattic.com");

        Assert.That(ClaudeAccountCatalog.PerCliLogins(accountsRoot), Is.EqualTo(new[] { "a@mindattic.com", "b@mindattic.com" }));
    }

    [Test]
    public void ConfigDirFor_mirrors_ClaudeLogin_Describe_s_folder_shape()
    {
        var dir = ClaudeAccountCatalog.ConfigDirFor("a@mindattic.com", accountsRoot);

        Assert.That(dir, Is.EqualTo(Path.Combine(accountsRoot, "a@mindattic.com")));
        Assert.That(ClaudeLogin.Describe(dir), Is.EqualTo("a@mindattic.com"));
    }

    [Test]
    public void List_always_includes_the_global_login_first()
    {
        ClaudeAccountCatalog.OrchestratorOverride = _ => null;
        ClaudeAccountCatalog.BusyOverride = _ => false;

        var options = ClaudeAccountCatalog.List(DateTime.UtcNow, accountsRoot);

        Assert.That(options[0].IsGlobal, Is.True);
        Assert.That(options[0].ConfigDir, Is.EqualTo(""));
    }

    [Test]
    public void List_degrades_gracefully_when_the_orchestrator_has_nothing_to_say()
    {
        Folder("a@mindattic.com");
        ClaudeAccountCatalog.OrchestratorOverride = _ => null; // "orchestrator not installed" / errored
        ClaudeAccountCatalog.BusyOverride = _ => false;

        var options = ClaudeAccountCatalog.List(DateTime.UtcNow, accountsRoot);

        Assert.That(options, Has.Count.EqualTo(2));
        Assert.That(options.All(o => !o.HasUsageData), Is.True);
        Assert.That(options.All(o => o.FiveHourLeftPercent == null && o.WeekLeftPercent == null), Is.True);
        // No pick available: the global login is the default preselection.
        Assert.That(options.Single(o => o.IsAutoPick).IsGlobal, Is.True);
    }

    [Test]
    public void List_fills_in_usage_and_flags_the_orchestrators_auto_pick()
    {
        Folder("a@mindattic.com");
        var now = new DateTime(2026, 10, 22, 12, 0, 0, DateTimeKind.Utc);
        ClaudeAccountCatalog.OrchestratorOverride = args =>
            args switch
            {
                ["accounts", "--json"] => """[{"Email":"a@mindattic.com","WeekPercent":13.0,"WeekResetUtc":"2026-10-26T00:00:00Z","FiveHourPercent":58.0,"FiveHourResetUtc":"2026-10-22T16:00:00Z"}]""",
                ["accounts", "pick", "--json"] => $$"""{"configDir":"{{Path.Combine(accountsRoot, "a@mindattic.com").Replace("\\", "\\\\")}}","email":"a@mindattic.com","reason":"most headroom"}""",
                _ => null
            };
        ClaudeAccountCatalog.BusyOverride = _ => false;

        var options = ClaudeAccountCatalog.List(now, accountsRoot);
        var picked = options.Single(o => o.Email == "a@mindattic.com");

        Assert.That(picked.HasUsageData, Is.True);
        Assert.That(picked.WeekLeftPercent, Is.EqualTo(87.0));
        Assert.That(picked.FiveHourLeftPercent, Is.EqualTo(42.0));
        Assert.That(picked.IsAutoPick, Is.True);
        Assert.That(options.Single(o => o.IsGlobal).IsAutoPick, Is.False);
    }

    [Test]
    public void List_reports_a_window_as_fully_left_once_its_reset_has_passed()
    {
        Folder("a@mindattic.com");
        var now = new DateTime(2026, 10, 22, 12, 0, 0, DateTimeKind.Utc);
        ClaudeAccountCatalog.OrchestratorOverride = args =>
            args switch
            {
                ["accounts", "--json"] => """[{"Email":"a@mindattic.com","WeekPercent":90.0,"WeekResetUtc":"2026-10-20T00:00:00Z","FiveHourPercent":null,"FiveHourResetUtc":null}]""",
                _ => null
            };
        ClaudeAccountCatalog.BusyOverride = _ => false;

        var options = ClaudeAccountCatalog.List(now, accountsRoot);
        var picked = options.Single(o => o.Email == "a@mindattic.com");

        Assert.That(picked.WeekLeftPercent, Is.EqualTo(100.0));
        Assert.That(picked.FiveHourLeftPercent, Is.Null);
    }

    [Test]
    public void List_flags_a_login_that_already_has_a_CLI()
    {
        var folder = Folder("a@mindattic.com");
        ClaudeAccountCatalog.OrchestratorOverride = _ => null;
        ClaudeAccountCatalog.BusyOverride = dir => dir == folder;

        var options = ClaudeAccountCatalog.List(DateTime.UtcNow, accountsRoot);

        Assert.That(options.Single(o => o.Email == "a@mindattic.com").HasCli, Is.True);
        Assert.That(options.Single(o => o.IsGlobal).HasCli, Is.False);
    }

    [Test]
    public void List_reads_the_global_logins_email_via_the_override()
    {
        ClaudeAccountCatalog.OrchestratorOverride = _ => null;
        ClaudeAccountCatalog.BusyOverride = _ => false;
        ClaudeAccountCatalog.GlobalEmailOverride = () => "me@mindattic.com";

        var options = ClaudeAccountCatalog.List(DateTime.UtcNow, accountsRoot);

        var global = options.Single(o => o.IsGlobal);
        Assert.That(global.Email, Is.EqualTo("me@mindattic.com"));
        Assert.That(global.Describe(), Is.EqualTo("the global login (me@mindattic.com)"));
    }

    // ---- ClaudeLoginMarker ------------------------------------------------------

    [Test]
    public void A_login_marker_makes_IsBusy_true_until_disposed()
    {
        var configDir = Path.Combine(accountsRoot, "marker@mindattic.com");

        Assert.That(ClaudeLoginMarker.IsBusy(configDir), Is.False);
        using (new ClaudeLoginMarker(configDir))
        {
            Assert.That(ClaudeLoginMarker.IsBusy(configDir), Is.True);
        }
        Assert.That(ClaudeLoginMarker.IsBusy(configDir), Is.False);
    }

    [Test]
    public void Different_logins_dont_collide()
    {
        var a = Path.Combine(accountsRoot, "a@mindattic.com");
        var b = Path.Combine(accountsRoot, "b@mindattic.com");

        using var markerA = new ClaudeLoginMarker(a);
        Assert.That(ClaudeLoginMarker.IsBusy(a), Is.True);
        Assert.That(ClaudeLoginMarker.IsBusy(b), Is.False);
    }

    [Test]
    public void The_global_logins_marker_is_keyed_separately_from_any_email()
    {
        using var marker = new ClaudeLoginMarker("");

        Assert.That(ClaudeLoginMarker.IsBusy(""), Is.True);
        Assert.That(ClaudeLoginMarker.IsBusy(Path.Combine(accountsRoot, "global")), Is.False);
    }

    // ---- ClaudeAccountResolver ---------------------------------------------------

    [Test]
    public void Resolve_with_account_fails_loud_on_an_unknown_email()
    {
        var result = ClaudeAccountResolver.Resolve("ghost@mindattic.com", interactive: false, accountsRoot: accountsRoot);

        Assert.That(result.Error, Does.Contain("ghost@mindattic.com"));
        Assert.That(result.ShowInTitle, Is.False);
    }

    [Test]
    public void Resolve_with_account_resolves_straight_to_its_folder_no_question_asked()
    {
        var folder = Folder("a@mindattic.com");
        var choosePrompted = false;

        var result = ClaudeAccountResolver.Resolve(
            "a@mindattic.com", interactive: true,
            choose: (_, _) => { choosePrompted = true; return null; },
            accountsRoot: accountsRoot);

        Assert.That(result.Error, Is.Null);
        Assert.That(result.ConfigDir, Is.EqualTo(folder));
        Assert.That(result.ShowInTitle, Is.True);
        Assert.That(choosePrompted, Is.False);
    }

    [Test]
    public void Resolve_skips_the_question_when_only_the_global_login_is_signed_in()
    {
        var choosePrompted = false;

        var result = ClaudeAccountResolver.Resolve(
            null, interactive: true,
            choose: (_, _) => { choosePrompted = true; return null; },
            accountsRoot: accountsRoot);

        Assert.That(result.Error, Is.Null);
        Assert.That(result.ConfigDir, Is.EqualTo(""));
        Assert.That(result.ShowInTitle, Is.False);
        Assert.That(choosePrompted, Is.False);
    }

    [Test]
    public void Resolve_offers_the_catalog_when_more_than_one_login_is_signed_in_and_interactive()
    {
        var folder = Folder("a@mindattic.com");
        ClaudeAccountCatalog.OrchestratorOverride = _ => null;
        ClaudeAccountCatalog.BusyOverride = _ => false;
        IReadOnlyList<ClaudeAccountOption>? seenOptions = null;

        var result = ClaudeAccountResolver.Resolve(
            null, interactive: true,
            choose: (options, auto) =>
            {
                seenOptions = options;
                return options.Single(o => o.Email == "a@mindattic.com");
            },
            accountsRoot: accountsRoot);

        Assert.That(seenOptions, Is.Not.Null);
        Assert.That(seenOptions!, Has.Count.EqualTo(2));
        Assert.That(result.ConfigDir, Is.EqualTo(folder));
        Assert.That(result.ShowInTitle, Is.True);
    }

    [Test]
    public void Resolve_falls_back_to_Auto_when_not_interactive_even_with_multiple_logins()
    {
        Folder("a@mindattic.com");
        ClaudeAccountCatalog.OrchestratorOverride = _ => null; // no pick -> Auto defaults to global
        ClaudeAccountCatalog.BusyOverride = _ => false;
        var choosePrompted = false;

        var result = ClaudeAccountResolver.Resolve(
            null, interactive: false,
            choose: (_, _) => { choosePrompted = true; return null; },
            accountsRoot: accountsRoot);

        Assert.That(choosePrompted, Is.False);
        Assert.That(result.ConfigDir, Is.EqualTo(""));
        Assert.That(result.ShowInTitle, Is.True);
    }

    [Test]
    public void Resolve_falls_back_to_Auto_when_the_chooser_declines()
    {
        var folder = Folder("a@mindattic.com");
        ClaudeAccountCatalog.OrchestratorOverride = args =>
            args switch
            {
                ["accounts", "pick", "--json"] => $$"""{"configDir":"{{folder.Replace("\\", "\\\\")}}","email":"a@mindattic.com","reason":"auto"}""",
                _ => null
            };
        ClaudeAccountCatalog.BusyOverride = _ => false;

        var result = ClaudeAccountResolver.Resolve(
            null, interactive: true,
            choose: (_, _) => null, // user pressed Enter / declined
            accountsRoot: accountsRoot);

        Assert.That(result.ConfigDir, Is.EqualTo(folder));
    }

    // ---- ClaudeAccountPrompt -------------------------------------------------------

    [Test]
    public void Prompt_returns_Auto_on_a_blank_line()
    {
        var options = new List<ClaudeAccountOption>
        {
            new("", "", true, null, null, false, false, true),
            new("a@mindattic.com", @"C:\accounts\a@mindattic.com", false, 42, 87, true, false, false)
        };

        var chosen = ClaudeAccountPrompt.Ask(options, options[0], new StringReader("\n"), TextWriter.Null);

        Assert.That(chosen, Is.EqualTo(options[0]));
    }

    [Test]
    public void Prompt_returns_the_typed_choice()
    {
        var options = new List<ClaudeAccountOption>
        {
            new("", "", true, null, null, false, false, true),
            new("a@mindattic.com", @"C:\accounts\a@mindattic.com", false, 42, 87, true, false, false)
        };

        var chosen = ClaudeAccountPrompt.Ask(options, options[0], new StringReader("2\n"), TextWriter.Null);

        Assert.That(chosen, Is.EqualTo(options[1]));
    }

    [Test]
    public void Prompt_lists_usage_and_busy_state()
    {
        var options = new List<ClaudeAccountOption>
        {
            new("", "", true, null, null, false, false, true),
            new("a@mindattic.com", @"C:\accounts\a@mindattic.com", false, 42, 87, true, true, false)
        };
        var output = new StringWriter();

        ClaudeAccountPrompt.Ask(options, options[0], new StringReader("\n"), output);

        var text = output.ToString();
        Assert.That(text, Does.Contain("5h left 42%, week left 87%"));
        Assert.That(text, Does.Contain("already has a CLI"));
        Assert.That(text, Does.Contain("(Auto)"));
    }

    [Test]
    public void Prompt_falls_back_to_Auto_on_an_out_of_range_number()
    {
        var options = new List<ClaudeAccountOption>
        {
            new("", "", true, null, null, false, false, true),
            new("a@mindattic.com", @"C:\accounts\a@mindattic.com", false, 42, 87, true, false, false)
        };

        var chosen = ClaudeAccountPrompt.Ask(options, options[0], new StringReader("99\n"), TextWriter.Null);

        Assert.That(chosen, Is.EqualTo(options[0]));
    }
}
