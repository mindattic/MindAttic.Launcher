#Requires -Version 5.1
<#
.SYNOPSIS
    Republishes artifacts\MindAttic.Launcher.exe and then runs it, AFTER the
    previous menu process has exited.

.DESCRIPTION
    The in-app "Restart" command can't republish itself synchronously: the menu
    runs from artifacts\MindAttic.Launcher.exe, and Windows locks a running exe
    image — dotnet publish can't overwrite it, so the publish silently fails and
    Restart respawns the *stale* binary. This script breaks that deadlock. It is
    launched in a fresh wt tab while the old menu is still up, waits for that
    process (-WaitPid) to exit so the lock on the exe releases, FORCE-republishes
    via ensure-fresh.ps1 -Force, then hands this tab over to the freshly published
    console. Restart forces the publish (rather than the launcher's conditional
    heuristic) because it is an explicit "rebuild now" request: if the rebuild
    fails it stops loudly instead of silently relaunching the stale binary.

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
$exe  = Join-Path $repo 'artifacts\MindAttic.Launcher.exe'

# Wait for the previous menu to exit so it releases the lock on the published
# exe. Cap the wait — if it somehow lingers we still try to publish (and fall
# back to whatever exe exists) rather than hang the new tab forever.
try { Wait-Process -Id $WaitPid -Timeout 30 -ErrorAction SilentlyContinue } catch { }

# Force a republish: Restart means "rebuild now", so we skip ensure-fresh's
# source-newer-than-exe heuristic (a stale-mtime read or a git pull/checkout that
# didn't bump working-tree mtimes past the exe would otherwise relaunch the OLD
# binary — the exact bug Restart is meant to avoid). Run it in a CHILD process:
# ensure-fresh calls `exit` on a publish failure, which would tear down THIS
# script before we could report it, and a child also gives us a clean exit code.
$fresh = Join-Path $here 'ensure-fresh.ps1'
& powershell -NoProfile -ExecutionPolicy Bypass -File $fresh -Force
$publishExit = $LASTEXITCODE

if (($publishExit -ne 0) -or (-not (Test-Path $exe))) {
    # Loud, never silent: the whole point of Restart is to run FRESH code, so a
    # failed rebuild must be seen — we do NOT paper over it by launching stale.
    Write-Host ''
    Write-Host "  Restart: rebuild FAILED (publish exit $publishExit)." -ForegroundColor Red
    if (Test-Path $exe) {
        Write-Host '  Not launching the stale console. Fix the build above, then relaunch.' -ForegroundColor Yellow
    } else {
        Write-Host "  No console exe present at $exe." -ForegroundColor Yellow
    }
    Write-Host ''
    Write-Host '  Press any key to close this tab...' -ForegroundColor DarkGray
    [void][System.Console]::ReadKey($true)
    exit 1
}

# Hand this tab over to the freshly published console.
& $exe
