#Requires -Version 5.1
<#
.SYNOPSIS
    Republishes artifacts\MindAttic.Console.exe and then runs it, AFTER the
    previous menu process has exited.

.DESCRIPTION
    The in-app "Restart" command can't republish itself synchronously: the menu
    runs from artifacts\MindAttic.Console.exe, and Windows locks a running exe
    image — dotnet publish can't overwrite it, so the publish silently fails and
    Restart respawns the *stale* binary. This script breaks that deadlock. It is
    launched in a fresh wt tab while the old menu is still up, waits for that
    process (-WaitPid) to exit so the lock on the exe releases, republishes via
    ensure-fresh.ps1, then hands this tab over to the freshly published console.

.PARAMETER WaitPid
    PID of the menu process being replaced. The script blocks until it exits
    (capped) before publishing.
#>

param(
    [Parameter(Mandatory)][int]$WaitPid
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$repo = (Resolve-Path (Join-Path $here '..')).Path
$exe  = Join-Path $repo 'artifacts\MindAttic.Console.exe'

# Wait for the previous menu to exit so it releases the lock on the published
# exe. Cap the wait — if it somehow lingers we still try to publish (and fall
# back to whatever exe exists) rather than hang the new tab forever.
try { Wait-Process -Id $WaitPid -Timeout 30 -ErrorAction SilentlyContinue } catch { }

# Republish if source changed. Don't let a publish failure abort the relaunch —
# a stale-but-runnable console beats a dead tab.
try { & (Join-Path $here 'ensure-fresh.ps1') } catch { }

if (-not (Test-Path $exe)) {
    Write-Host "MindAttic.Console.exe not found at $exe (publish failed?)." -ForegroundColor Red
    exit 1
}

# Hand this tab over to the freshly published console.
& $exe
