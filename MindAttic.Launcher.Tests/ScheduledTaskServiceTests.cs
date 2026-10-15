using MindAttic.Launcher.Services;
using NUnit.Framework;

namespace MindAttic.Launcher.Tests;

[TestFixture]
public sealed class ScheduledTaskServiceTests
{
    // schtasks /Query /FO CSV /V /NH columns (0-indexed): 0 HostName, 1 TaskName,
    // 2 Next Run Time, 3 Status, 4 Logon Mode, 5 Last Run Time, 6 Last Result, 7 Author, 8 Task To Run, ...
    private static string Row(string taskName, string status, string nextRun, string lastRun, string lastResult) =>
        $"\"WORKSTATION\",\"{taskName}\",\"{nextRun}\",\"{status}\",\"Interactive/Background\",\"{lastRun}\",\"{lastResult}\",\"MindAttic\",\"C:\\Apps\\MindAttic.Launcher\\MindAttic.Launcher.exe backup\"";

    [Test]
    public void ParseQueryCsv_reads_enabled_state_last_run_and_next_run()
    {
        var csv = Row(@"\MindAttic\Launcher\MindAtticLauncherNightlyBackup", "Ready",
            "10/15/2026 12:00:00 AM", "10/14/2026 12:00:00 AM", "0");

        var status = ScheduledTaskService.ParseQueryCsv(csv);

        Assert.Multiple(() =>
        {
            Assert.That(status.Registered, Is.True);
            Assert.That(status.Enabled, Is.True);
            Assert.That(status.LastTaskResult, Is.EqualTo(0));
            Assert.That(status.LastRunTime, Is.Not.Null);
            Assert.That(status.NextRunTime, Is.Not.Null);
        });
    }

    [Test]
    public void ParseQueryCsv_reports_disabled_when_status_is_Disabled()
    {
        var csv = Row(@"\MindAttic\Launcher\MindAtticLauncherNightlyBackup", "Disabled",
            "", "10/14/2026 12:00:00 AM", "0");

        var status = ScheduledTaskService.ParseQueryCsv(csv);

        Assert.Multiple(() =>
        {
            Assert.That(status.Registered, Is.True);
            Assert.That(status.Enabled, Is.False);
        });
    }

    [Test]
    public void ParseQueryCsv_reports_not_registered_for_empty_output()
    {
        var status = ScheduledTaskService.ParseQueryCsv("");
        Assert.That(status.Registered, Is.False);
    }

    [Test]
    public void BuildQueryArguments_targets_the_folder_qualified_task_name()
    {
        Assert.That(ScheduledTaskService.BuildQueryArguments(),
            Is.EqualTo(new[] { "/Query", "/TN", @"\MindAttic\Launcher\MindAtticLauncherNightlyBackup", "/FO", "CSV", "/V", "/NH" }));
    }

    [TestCase(true, ExpectedResult = "/Enable")]
    [TestCase(false, ExpectedResult = "/Disable")]
    public string BuildEnableArguments_toggles_enable_vs_disable(bool enabled) =>
        ScheduledTaskService.BuildEnableArguments(enabled)[^1];

    [Test]
    public void BuildUnregisterArguments_forces_delete()
    {
        Assert.That(ScheduledTaskService.BuildUnregisterArguments(),
            Is.EqualTo(new[] { "/Delete", "/TN", @"\MindAttic\Launcher\MindAtticLauncherNightlyBackup", "/F" }));
    }
}
