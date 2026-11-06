using MindAttic.Launcher.Services;
using NUnit.Framework;

namespace MindAttic.Launcher.Tests;

/// <summary>
/// Covers <see cref="AgentProvisioning"/> (M.LNCH-T113): every scenario runs against a temp
/// "workspace" with its own fake <c>mindattic-agent-standard</c> folder and marker file — never the
/// real <c>mindattic-agent-standard</c>/<c>~/.claude</c> etc., which may be live sessions' own config
/// (see the ticket's own safety constraint). <see cref="AgentProvisioning.EnsureProvisioned"/> takes
/// an injected <c>runScript</c>, so these tests never actually invoke powershell.
/// </summary>
[TestFixture]
public sealed class AgentProvisioningTests
{
    private string workspaceRoot = "";
    private string scriptsDir = "";
    private string abilitiesDir = "";
    private string hooksDir = "";
    private string markerPath = "";

    [SetUp]
    public void SetUp()
    {
        workspaceRoot = Path.Combine(Path.GetTempPath(), "MindAttic.Launcher.Tests", Guid.NewGuid().ToString("N"));
        scriptsDir = Path.Combine(workspaceRoot, AgentProvisioning.ScriptsDirName);
        abilitiesDir = Path.Combine(scriptsDir, "shared", "abilities");
        hooksDir = Path.Combine(scriptsDir, "shared", "hooks");
        Directory.CreateDirectory(abilitiesDir);
        Directory.CreateDirectory(hooksDir);
        File.WriteAllText(Path.Combine(abilitiesDir, "commit.md"), "---\nname: commit\n---\nbody");
        File.WriteAllText(Path.Combine(hooksDir, "pace-gate.py"), "# pace gate");
        markerPath = Path.Combine(workspaceRoot, "marker", "agent-provisioning.marker");
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(workspaceRoot, true); } catch (IOException) { }
    }

    // ---- IsStale ----------------------------------------------------------------------------

    [Test]
    public void IsStale_true_when_no_marker_exists_yet()
    {
        Assert.That(AgentProvisioning.IsStale(scriptsDir, markerPath), Is.True);
    }

    [Test]
    public void IsStale_false_once_the_marker_is_newer_than_every_watched_file()
    {
        WriteMarkerInThePast();
        Touch(markerPath, DateTime.UtcNow.AddMinutes(5)); // newer than the abilities/hooks files written in SetUp

        Assert.That(AgentProvisioning.IsStale(scriptsDir, markerPath), Is.False);
    }

    [Test]
    public void IsStale_true_again_once_an_ability_file_changes_after_the_marker()
    {
        WriteMarkerInThePast();
        Touch(markerPath, DateTime.UtcNow);

        // Simulate an edit landing after the last provisioning run.
        File.WriteAllText(Path.Combine(abilitiesDir, "commit.md"), "---\nname: commit\n---\nedited body");
        Touch(Path.Combine(abilitiesDir, "commit.md"), DateTime.UtcNow.AddMinutes(1));

        Assert.That(AgentProvisioning.IsStale(scriptsDir, markerPath), Is.True);
    }

    [Test]
    public void IsStale_true_again_once_a_hook_file_changes_after_the_marker()
    {
        Touch(markerPath, DateTime.UtcNow);
        File.WriteAllText(Path.Combine(hooksDir, "pace-gate.py"), "# edited pace gate");
        Touch(Path.Combine(hooksDir, "pace-gate.py"), DateTime.UtcNow.AddMinutes(1));

        Assert.That(AgentProvisioning.IsStale(scriptsDir, markerPath), Is.True);
    }

    // ---- EnsureProvisioned: attempted when due -----------------------------------------------

    [Test]
    public void EnsureProvisioned_runs_render_then_install_hooks_and_writes_the_marker_when_stale()
    {
        var calls = new List<string>();
        AgentProvisioning.EnsureProvisioned(
            workspaceRoot,
            markerPath,
            warn: msg => Assert.Fail($"should not warn on a clean run: {msg}"),
            runScript: script => { calls.Add(script); return (true, null); });

        Assert.Multiple(() =>
        {
            Assert.That(calls, Is.EqualTo(new[] { "render-abilities.ps1", "install-hooks.ps1" }));
            Assert.That(File.Exists(markerPath), Is.True, "a successful run must leave a marker behind");
        });
    }

    [Test]
    public void EnsureProvisioned_skips_the_scripts_entirely_when_nothing_changed_since_the_marker()
    {
        Touch(markerPath, DateTime.UtcNow.AddMinutes(5)); // newer than every watched file from SetUp

        var ran = false;
        AgentProvisioning.EnsureProvisioned(
            workspaceRoot,
            markerPath,
            warn: msg => Assert.Fail($"should not warn when nothing is stale: {msg}"),
            runScript: _ => { ran = true; return (true, null); });

        Assert.That(ran, Is.False, "a redundant run must never shell out to either script");
    }

    [Test]
    public void EnsureProvisioned_runs_again_once_a_watched_file_changes()
    {
        Touch(markerPath, DateTime.UtcNow);
        File.WriteAllText(Path.Combine(abilitiesDir, "new-ability.md"), "---\nname: new-ability\n---\nbody");
        Touch(Path.Combine(abilitiesDir, "new-ability.md"), DateTime.UtcNow.AddMinutes(1));

        var ran = false;
        AgentProvisioning.EnsureProvisioned(workspaceRoot, markerPath, warn: _ => { }, runScript: _ => { ran = true; return (true, null); });

        Assert.That(ran, Is.True);
    }

    // ---- EnsureProvisioned: a failure never throws / never blocks the caller -----------------

    [Test]
    public void EnsureProvisioned_warns_and_returns_quietly_when_render_abilities_fails()
    {
        var warnings = new List<string>();
        var hooksRan = false;

        Assert.DoesNotThrow(() => AgentProvisioning.EnsureProvisioned(
            workspaceRoot,
            markerPath,
            warn: warnings.Add,
            runScript: script =>
            {
                if (script == "install-hooks.ps1") hooksRan = true;
                return script == "render-abilities.ps1" ? (false, "boom") : (true, null);
            }));

        Assert.Multiple(() =>
        {
            Assert.That(warnings, Has.Count.EqualTo(1));
            Assert.That(warnings[0], Does.Contain("render-abilities.ps1"));
            Assert.That(hooksRan, Is.False, "install-hooks should not run after render-abilities already failed");
            Assert.That(File.Exists(markerPath), Is.False, "a failed run must not be recorded as provisioned");
        });
    }

    [Test]
    public void EnsureProvisioned_warns_and_returns_quietly_when_install_hooks_fails()
    {
        var warnings = new List<string>();

        Assert.DoesNotThrow(() => AgentProvisioning.EnsureProvisioned(
            workspaceRoot,
            markerPath,
            warn: warnings.Add,
            runScript: script => script == "install-hooks.ps1" ? (false, "boom") : (true, null)));

        Assert.Multiple(() =>
        {
            Assert.That(warnings, Has.Count.EqualTo(1));
            Assert.That(warnings[0], Does.Contain("install-hooks.ps1"));
            Assert.That(File.Exists(markerPath), Is.False);
        });
    }

    [Test]
    public void EnsureProvisioned_warns_and_swallows_an_unexpected_exception_from_runScript()
    {
        var warnings = new List<string>();

        Assert.DoesNotThrow(() => AgentProvisioning.EnsureProvisioned(
            workspaceRoot,
            markerPath,
            warn: warnings.Add,
            runScript: _ => throw new InvalidOperationException("scripts folder got deleted mid-run")));

        Assert.That(warnings, Has.Count.EqualTo(1));
    }

    [Test]
    public void EnsureProvisioned_is_a_silent_no_op_when_the_scripts_folder_is_missing()
    {
        var missingWorkspace = Path.Combine(Path.GetTempPath(), "MindAttic.Launcher.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(missingWorkspace); // the workspace exists; mindattic-agent-standard under it does not

        try
        {
            var ran = false;
            Assert.DoesNotThrow(() => AgentProvisioning.EnsureProvisioned(
                missingWorkspace,
                Path.Combine(missingWorkspace, "marker"),
                warn: msg => Assert.Fail($"a missing scripts folder on a dev machine shouldn't warn on every launch: {msg}"),
                runScript: _ => { ran = true; return (true, null); }));

            Assert.That(ran, Is.False);
        }
        finally
        {
            try { Directory.Delete(missingWorkspace, true); } catch (IOException) { }
        }
    }

    // ---- Fire-and-forget entry point: never blocks the caller ---------------------------------

    [Test]
    public void EnsureProvisionedFireAndForget_returns_immediately_without_throwing()
    {
        // No mindattic-agent-standard folder under this temp root, so the background task is a fast
        // no-op either way; the point of this test is that the call itself never blocks or throws
        // synchronously, which is what a caller mid-launch actually depends on.
        Assert.DoesNotThrow(() => AgentProvisioning.EnsureProvisionedFireAndForget(workspaceRoot));
    }

    // ---- helpers -------------------------------------------------------------------------------

    private void WriteMarkerInThePast() => Touch(markerPath, DateTime.UtcNow.AddHours(-1));

    private static void Touch(string path, DateTime utc)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (!File.Exists(path)) File.WriteAllText(path, "");
        File.SetLastWriteTimeUtc(path, utc);
    }
}
