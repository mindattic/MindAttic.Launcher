#Requires -Version 5.1
<#
.SYNOPSIS
    Builds and deploys MindAttic.Launcher to C:\Apps\MindAttic.Launcher\, the same
    place every other MindAttic app lives, and optionally runs it. The one script
    for publishing, launching and restarting the Launcher.

.DESCRIPTION
    Deploys a framework-dependent single-file win-x64 exe, plus two generated files:
      deploy.bat  next to the exe; the desktop shortcut points at it. Runs this script
                  (which republishes only if source changed), then the exe, passing
                  its arguments through.
      repo.path   this repo's path. The deployed exe has no repo above it to walk up
                  to, so ExePath reads this to find this script, the workspace root
                  and git.

    Unlike the other apps' deploy scripts, this never closes running copies: every
    agent tab is a live `MindAttic.Launcher.exe host ...` process, so closing them
    would kill your agents. A locked exe is renamed aside instead (Windows allows
    renaming a running image) and the fresh one is published next to it; the
    *.stale-* leftovers are deleted on a later run once nothing uses them.

    Without -Force it only republishes when the exe, deploy.bat or repo.path is
    missing, or a source file is newer than the exe, so the per-launch call from
    deploy.bat is a fast no-op.

.PARAMETER Force
    Republish with a clean bin/obj rebuild even if nothing looks changed.

.PARAMETER Launch
    After deploying, run the deployed exe in this console with -LaunchArgs.

.PARAMETER WaitPid
    The in-app Restart: wait for that menu process to exit, force a clean rebuild,
    then run the fresh exe. Restart means "run fresh code", so a failed rebuild
    stops loudly here instead of launching the stale exe.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File deploy.ps1 -Force
    powershell -ExecutionPolicy Bypass -File deploy.ps1 -Launch --tab Foo
#>
param(
    [switch]$Force,
    [switch]$Launch,
    [int]$WaitPid = 0,
    [Parameter(ValueFromRemainingArguments)]
    [string[]]$LaunchArgs = @()
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repo      = $PSScriptRoot
$proj      = Join-Path $repo 'MindAttic.Launcher\MindAttic.Launcher.csproj'
$projDir   = Join-Path $repo 'MindAttic.Launcher'
$props     = Join-Path $repo 'Directory.Build.props'
$deployDir = 'C:\Apps\MindAttic.Launcher'
$exeName   = 'MindAttic.Launcher.exe'
$exe       = Join-Path $deployDir $exeName
$pdb       = [System.IO.Path]::ChangeExtension($exe, '.pdb')

# ── Restart: wait for the old menu ──────────────────────────────────────────
if ($WaitPid -gt 0) {
    # Cap the wait: if it lingers, still publish (the exe gets renamed aside).
    try { Wait-Process -Id $WaitPid -Timeout 30 -ErrorAction SilentlyContinue } catch { }
    $Force = $true
    $Launch = $true
}

function Test-NeedsPublish {
    if (-not (Test-Path $exe)) { return $true }
    foreach ($f in 'deploy.bat', 'repo.path') {
        if (-not (Test-Path (Join-Path $deployDir $f))) { return $true }
    }
    $exeTime = (Get-Item $exe).LastWriteTimeUtc
    if ((Test-Path $props) -and (Get-Item $props).LastWriteTimeUtc -gt $exeTime) { return $true }

    $newer = Get-ChildItem -Path $projDir -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object {
            $_.FullName -notmatch '\\(bin|obj)\\' -and
            ($_.Extension -eq '.cs' -or $_.Extension -eq '.csproj') -and
            $_.LastWriteTimeUtc -gt $exeTime
        } |
        Select-Object -First 1
    return [bool]$newer
}

# True when the file can't be opened for writing, i.e. a running exe image.
function Test-Locked([string]$path) {
    try {
        $fs = [System.IO.File]::Open($path, 'Open', 'Write', 'None')
        $fs.Dispose()
        return $false
    } catch { return $true }
}

# dotnet publish can't overwrite a running exe, but it can be renamed: the live
# process keeps running from the moved image. Unlocked files are left for publish
# to overwrite directly, so there's no .stale-* churn.
function Move-Aside([string]$path) {
    if (-not (Test-Path $path)) { return }
    if (-not (Test-Locked $path)) { return }
    $stamp = Get-Date -Format 'yyyyMMddHHmmssfff'
    try { Move-Item -LiteralPath $path -Destination "$path.stale-$stamp" -Force -ErrorAction Stop } catch { }
}

function Publish {
    if ($Force) {
        foreach ($d in @((Join-Path $projDir 'bin'), (Join-Path $projDir 'obj'))) {
            if (Test-Path $d) {
                try { Remove-Item -Recurse -Force $d -ErrorAction Stop }
                catch { Write-Host "  (could not fully remove $d : $($_.Exception.Message))" -ForegroundColor DarkYellow }
            }
        }
    }

    Move-Aside $exe
    Move-Aside $pdb

    Write-Host "Publishing MindAttic.Launcher -> $exe" -ForegroundColor Cyan
    dotnet publish $proj `
        --configuration Release `
        --runtime win-x64 `
        --self-contained false `
        /p:PublishSingleFile=true `
        /p:IncludeNativeLibrariesForSelfExtract=true `
        --output $deployDir | Out-Host
    if ($LASTEXITCODE -ne 0) { return $false }
    if (-not (Test-Path $exe)) { return $false }

    Set-Content -LiteralPath (Join-Path $deployDir 'repo.path') -Value $repo -Encoding UTF8
    $deployBat = @"
@echo off
title MindAttic.Launcher
rem Generated by $repo\deploy.ps1 - edits here are overwritten.
set "DEPLOY_PS1=$repo\deploy.ps1"
if exist "%DEPLOY_PS1%" (
    powershell -NoProfile -ExecutionPolicy Bypass -File "%DEPLOY_PS1%"
    if errorlevel 1 echo Redeploy failed - launching whatever is already in this folder instead.
) else (
    echo Source repo not found at "%DEPLOY_PS1%" - launching existing build without redeploying.
)
"%~dp0$exeName" %*
"@
    Set-Content -LiteralPath (Join-Path $deployDir 'deploy.bat') -Value $deployBat -Encoding ASCII
    Write-Host "Deployed $exe" -ForegroundColor Green
    return $true
}

# ── Deploy ──────────────────────────────────────────────────────────────────
New-Item -ItemType Directory -Force -Path $deployDir | Out-Null
Get-ChildItem -Path $deployDir -Filter '*.stale-*' -File -ErrorAction SilentlyContinue |
    ForEach-Object { try { Remove-Item $_.FullName -Force -ErrorAction Stop } catch { } }

$ok = $true
if ($Force -or (Test-NeedsPublish)) { $ok = Publish }

if (-not $ok) {
    Write-Host 'Deploy FAILED.' -ForegroundColor Red
    if ($WaitPid -gt 0) {
        Write-Host '  Restart: not launching the stale build. Fix the build above, then relaunch.' -ForegroundColor Yellow
        Write-Host '  Press any key to close this tab...' -ForegroundColor DarkGray
        [void][System.Console]::ReadKey($true)
        exit 1
    }
    if (-not $Launch) { exit 1 }
    Write-Host 'Launching whatever is already deployed instead.' -ForegroundColor Yellow
}

if (-not $Launch) { exit 0 }
if (-not (Test-Path $exe)) {
    Write-Host "Nothing deployed at $exe." -ForegroundColor Red
    exit 1
}
& $exe @LaunchArgs
exit $LASTEXITCODE
