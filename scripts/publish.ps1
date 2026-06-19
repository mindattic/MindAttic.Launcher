#Requires -Version 5.1
<#
.SYNOPSIS
    Publishes mindattic.exe as a single file (framework-dependent, win-x64)
    into the artifacts/ directory.

.DESCRIPTION
    Output: <repo>\artifacts\mindattic.exe

    Run from the repo root:
        powershell -NoProfile -ExecutionPolicy Bypass -File scripts\publish.ps1

.PARAMETER Clean
    Remove the project's bin/obj before publishing so MSBuild does a full,
    timestamp-independent recompile. `dotnet publish` is incrementally skipped
    when a source file's mtime is older than the prior build outputs (e.g. after
    restoring/checking out an older file) — leaving a stale exe. The in-app
    "Restart" passes -Clean because it's an explicit "rebuild now" request that
    must never produce stale code; the per-launch fast path does not.
#>

param(
    [switch]$Clean
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repo     = Resolve-Path (Join-Path $PSScriptRoot '..')
$proj     = Join-Path $repo 'MindAttic.Launcher\MindAttic.Launcher.csproj'
$projDir  = Join-Path $repo 'MindAttic.Launcher'
$outDir   = Join-Path $repo 'artifacts'
$exeName  = 'MindAttic.Launcher.exe'

if ($Clean) {
    Write-Host "Clean: removing bin/obj for a full rebuild"
    foreach ($d in @((Join-Path $projDir 'bin'), (Join-Path $projDir 'obj'))) {
        if (Test-Path $d) {
            try { Remove-Item -Recurse -Force $d -ErrorAction Stop }
            catch { Write-Host "  (could not fully remove $d : $($_.Exception.Message))" -ForegroundColor DarkYellow }
        }
    }
}

Write-Host "Publishing $proj"
Write-Host "  → $outDir\$exeName"

dotnet publish $proj `
    --configuration Release `
    --runtime win-x64 `
    --self-contained false `
    /p:PublishSingleFile=true `
    /p:IncludeNativeLibrariesForSelfExtract=true `
    --output $outDir | Out-Host

if ($LASTEXITCODE -ne 0) {
    Write-Host "Publish failed (exit $LASTEXITCODE)." -ForegroundColor Red
    exit $LASTEXITCODE
}

$exe = Join-Path $outDir $exeName
if (-not (Test-Path $exe)) {
    Write-Host "Publish completed but $exeName not found at $exe." -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "Published $exeName ($([math]::Round(((Get-Item $exe).Length / 1MB), 1)) MB)" -ForegroundColor Green
Write-Host "  $exe"
