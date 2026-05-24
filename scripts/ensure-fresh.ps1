#Requires -Version 5.1
<#
.SYNOPSIS
    Republishes artifacts\MindAttic.Terminal.exe when the exe is missing or any
    project source file (*.cs, *.csproj, Directory.Build.props) is newer than
    the exe. Otherwise it's a fast no-op.

.DESCRIPTION
    Called by MindAttic.Terminal.bat on every launch, and by the in-app
    "Restart" command before it respawns the Release exe in a new wt tab.
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$here  = Split-Path -Parent $MyInvocation.MyCommand.Path
$repo  = (Resolve-Path (Join-Path $here '..')).Path
$exe   = Join-Path $repo 'artifacts\MindAttic.Terminal.exe'
$src   = Join-Path $repo 'MindAttic.Terminal'
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

if (Test-NeedsPublish) {
    & (Join-Path $here 'publish.ps1')
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
