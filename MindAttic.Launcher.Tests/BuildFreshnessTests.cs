using MindAttic.Launcher.Services;
using NUnit.Framework;

namespace MindAttic.Launcher.Tests;

[TestFixture]
public sealed class BuildFreshnessTests
{
    private static readonly DateTimeOffset Built =
        new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public void Evaluate_returns_null_when_build_is_newer_than_head()
    {
        var head = Built.AddHours(-3);

        Assert.That(BuildFreshness.Evaluate(Built, head), Is.Null);
    }

    [Test]
    public void Evaluate_returns_null_when_build_equals_head()
    {
        Assert.That(BuildFreshness.Evaluate(Built, Built), Is.Null);
    }

    [Test]
    public void Evaluate_reports_whole_days_behind()
    {
        var head = Built.AddDays(11).AddHours(2);

        var result = BuildFreshness.Evaluate(Built, head);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.DaysBehind, Is.EqualTo(11));
    }

    [Test]
    public void Evaluate_floors_a_same_day_lag_to_zero_days()
    {
        var head = Built.AddHours(5);

        var result = BuildFreshness.Evaluate(Built, head);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.DaysBehind, Is.EqualTo(0));
    }

    [Test]
    public void Evaluate_compares_across_time_zone_offsets()
    {
        // Same instant expressed in a different offset must not read as "behind".
        var headSameInstant = Built.ToOffset(TimeSpan.FromHours(-5));

        Assert.That(BuildFreshness.Evaluate(Built, headSameInstant), Is.Null);
    }
}
