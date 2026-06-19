#Requires -Version 5.1
<#
.SYNOPSIS
    Republishes artifacts\MindAttic.Launcher.exe when the exe is missing or any
    project source file (*.cs, *.csproj, Directory.Build.props) is newer than
    the exe. Otherwise it's a fast no-op.

.DESCRIPTION
    Called by MindAttic.Launcher.bat on every launch, and by the in-app
    "Restart" command before it respawns the Release exe in a new wt tab.

.PARAMETER Force
    Publish unconditionally, skipping the source-newer-than-exe heuristic. The
    launcher relies on the heuristic (a fast no-op when nothing changed), but an
    explicit "Restart" is a direct "rebuild now" request: a stale-mtime read or a
    git pull/checkout that didn't bump working-tree mtimes past the exe must NOT
    quietly relaunch the old binary. Restart passes -Force.
#>

param(
    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$here  = Split-Path -Parent $MyInvocation.MyCommand.Path
$repo  = (Resolve-Path (Join-Path $here '..')).Path
$exe   = Join-Path $repo 'artifacts\MindAttic.Launcher.exe'
$src   = Join-Path $repo 'MindAttic.Launcher'
$props = Join-Path $repo 'Directory.Build.props'

function Test-NeedsPublish {
    if (-not (Test-Path $exe)) { return $true }
    $exeTime = (Get-Item $exe).LastWriteTimeUtc

    if ((Test-Path $props) -and (Get-Item $props).LastWriteTimeUtc -gt $exeTime) {
        return $true
    }

    $newer = Get-ChildItem -Path $src -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object {
            $_.FullName -notmatch '\\(bin|obj)\\' -and
            ($_.Extension -eq '.cs' -or $_.Extension -eq '.csproj') -and
            $_.LastWriteTimeUtc -gt $exeTime
        } |
        Select-Object -First 1

    return [bool]$newer
}

$artifacts = Split-Path -Parent $exe

# Clear away exe/pdb copies parked by a previous self-republish (see below).
# Best-effort: any still locked by a live menu are skipped and retried next run.
function Remove-StaleArtifacts {
    if (-not (Test-Path $artifacts)) { return }
    Get-ChildItem -Path $artifacts -Filter '*.stale-*' -File -ErrorAction SilentlyContinue |
        ForEach-Object { try { Remove-Item $_.FullName -Force -ErrorAction Stop } catch { } }
}

# True when the file can't be opened for writing — i.e. it's the running exe
# image (Windows holds an exclusive write/delete lock on it while it executes).
function Test-Locked([string]$path) {
    try {
        $fs = [System.IO.File]::Open($path, 'Open', 'Write', 'None')
        $fs.Dispose()
        return $false
    } catch { return $true }
}

# Rename a LOCKED file out of the way. The menu often runs FROM
# artifacts\MindAttic.Launcher.exe, and dotnet publish can't overwrite a running
# exe image — but it CAN be renamed: the live process keeps executing from the
# moved image, and publish then writes a fresh exe at the original path. When the
# file isn't locked (manual publish, or a restart after the old process exited)
# we leave it for publish to overwrite directly — no .stale-* churn. Leftovers
# are reaped by Remove-StaleArtifacts on a later run once they're unlocked.
function Move-Aside([string]$path) {
    if (-not (Test-Path $path)) { return }
    if (-not (Test-Locked $path)) { return }
    $stamp = Get-Date -Format 'yyyyMMddHHmmssfff'
    try { Move-Item -LiteralPath $path -Destination "$path.stale-$stamp" -Force -ErrorAction Stop } catch { }
}

Remove-StaleArtifacts

if ($Force -or (Test-NeedsPublish)) {
    $pdb = [System.IO.Path]::ChangeExtension($exe, '.pdb')
    Move-Aside $exe
    Move-Aside $pdb
    # A forced (Restart) publish does a clean, timestamp-independent rebuild so no
    # stale-mtime state can leave the old code in place; the launcher's auto path
    # publishes incrementally for speed.
    & (Join-Path $here 'publish.ps1') -Clean:$Force
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
