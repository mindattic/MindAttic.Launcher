#Requires -Version 5.1
<#
.SYNOPSIS
    Builds and deploys MindAttic.Launcher to C:\Apps\MindAttic.Launcher\, the same
    place every other MindAttic app lives, and optionally runs it. The one script
    for publishing, launching and restarting the Launcher.

.DESCRIPTION
    Deploys a framework-dependent single-file win-x64 exe, plus two generated files:
      deploy.bat  next to the exe; the desktop shortcut points at it. Started with no
                  arguments (a double-click) it shows a menu: 1) Deploy & Run runs
                  this script with -Force, then the exe; 2) Run starts the exe
                  without running this script. Started with arguments (the
                  workspace-root MindAttic.Launcher.bat's --tab, host/commit/backup
                  ... invocations) it never prompts: it runs this script (which
                  republishes only if source changed), then the exe, passing its
                  arguments through (MAT-T67). Written on every run, so an existing
                  install picks up a new launcher without a republish.
      repo.path   this repo's path. The deployed exe has no repo above it to walk up
                  to, so ExePath reads this to find this script, the workspace root
                  and git.

    Unlike the other apps' deploy scripts, this never closes running copies: every
    agent tab is a live `MindAttic.Launcher.exe host ...` process, so closing them
    would kill your agents. A locked exe is renamed aside instead (Windows allows
    renaming a running image) and the fresh one is published next to it; the
    *.stale-* leftovers are deleted on a later run once nothing uses them.

    Without -Force it only republishes when the exe or repo.path is missing, or a
    source file is newer than the exe, so the per-launch call from deploy.bat is a
    fast no-op.

.PARAMETER Force
    Republish with a clean bin/obj rebuild even if nothing looks changed.

.PARAMETER Launch
    After deploying, run the deployed exe in this console with -LaunchArgs.

.PARAMETER LauncherOnly
    Only (re)write deploy.bat and repo.path in C:\Apps\MindAttic.Launcher, then exit:
    nothing is built, moved aside or launched.

.PARAMETER WaitPid
    The in-app Restart: wait for that menu process to exit, force a clean rebuild,
    then run the fresh exe. Restart means "run fresh code", so a failed rebuild
    stops loudly here instead of launching the stale exe.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File deploy.ps1 -Force
    powershell -ExecutionPolicy Bypass -File deploy.ps1 -LauncherOnly
    powershell -ExecutionPolicy Bypass -File deploy.ps1 -Launch --tab Foo
#>
param(
    [switch]$Force,
    [switch]$Launch,
    [int]$WaitPid = 0,
    [switch]$LauncherOnly,
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
    if (-not (Test-Path (Join-Path $deployDir 'repo.path'))) { return $true }
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

    Write-Host "Deployed $exe" -ForegroundColor Green
    return $true
}

# ── The launcher (MAT-T67) ──────────────────────────────────────────────────
# Written on every run (alone with -LauncherOnly), not just when publishing, so an
# existing install picks up a new launcher. The menu lives only in the .bat: this
# script never prompts, so agents and /deploy run it unattended. With arguments the
# .bat is a CLI call (MindAttic.Launcher.bat --tab Foo, host/commit/backup ...), so
# it skips the menu and behaves as it always has.
function Write-Launcher {
    Set-Content -LiteralPath (Join-Path $deployDir 'repo.path') -Value $repo -Encoding UTF8
    $deployBat = @"
@echo off
title MindAttic.Launcher
rem Generated by $repo\deploy.ps1 - edits here are overwritten.
set "DEPLOY_PS1=$repo\deploy.ps1"
set "APP_EXE=%~dp0$exeName"
set "DEPLOY_ARGS="
set "DEPLOYED="
if not "%~1"=="" goto deploy
echo.
echo   MindAttic.Launcher
echo     1^) Deploy ^& Run
echo     2^) Run
echo.
choice /c 12 /n /m "  Choose 1 or 2: "
if errorlevel 2 goto run
set "DEPLOY_ARGS=-Force"
:deploy
set "DEPLOYED=1"
if exist "%DEPLOY_PS1%" (
    powershell -NoProfile -ExecutionPolicy Bypass -File "%DEPLOY_PS1%" %DEPLOY_ARGS%
    if errorlevel 1 echo Redeploy failed - launching whatever is already in this folder instead.
) else (
    echo Source repo not found at "%DEPLOY_PS1%" - launching existing build without redeploying.
)
:run
if not exist "%APP_EXE%" (
    if not defined DEPLOYED if exist "%DEPLOY_PS1%" (
        echo Nothing is deployed at "%APP_EXE%" yet - deploying first.
        goto deploy
    )
    echo Nothing is deployed at "%APP_EXE%".
    pause
    exit /b 1
)
"%APP_EXE%" %*
"@
    # CRLF throughout: cmd misreads goto labels in an LF-only batch file.
    $deployBat = ($deployBat -replace "`r?`n", "`r`n") + "`r`n"
    $bat = Join-Path $deployDir 'deploy.bat'
    if (-not (Test-Path $bat) -or (Get-Content -Raw $bat) -ne $deployBat) {
        Set-Content -LiteralPath $bat -Value $deployBat -Encoding ASCII -NoNewline
    }
}

# ── Deploy ──────────────────────────────────────────────────────────────────
New-Item -ItemType Directory -Force -Path $deployDir | Out-Null
Write-Launcher
if ($LauncherOnly) { exit 0 }

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
