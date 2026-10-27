#Requires -Version 5.1
<#
.SYNOPSIS
    First-time setup for a fresh MindAttic.Launcher checkout: asks the three
    things a new machine needs answered, then deploys and wires them up.

.DESCRIPTION
    Interactive, meant to be double-clicked (via setup.bat) or run once after
    `git clone`. Asks:
      1. Workspace directory  - the folder that holds this checkout and every
                                 sibling MindAttic-style project repo.
      2. Desktop icon          - whether to drop a shortcut to the deployed app
                                 on the Desktop.
      3. Example repo          - an existing git repo to seed the roster with,
                                 so Open Project Tab has something to open on
                                 the very first run.
      4. GitHub token          - optional; lets Launcher read your repos' topics.
                                 If given, this script also tags every repo it
                                 can see as 'wip' right away (the roster's
                                 filter topic - see TopicRepoSync.cs), so the
                                 full roster is there on the very first launch
                                 instead of tagging each repo by hand first.

    Then it:
      - Writes a one-time settings.json seed next to this script (only if no
        real settings have been created yet at
        %APPDATA%\MindAttic\MindAttic.Launcher\settings.json - an existing
        install is never touched).
      - Runs run.ps1 -Force to publish to C:\Apps\MindAttic.Launcher.
      - If a GitHub token was given, runs 'adopt-repos' to save it and tag
        every repo it can see as 'wip'.
      - Creates the Desktop shortcut, if asked for.
      - Offers to run 'bootstrap' (install sqlcmd/LocalDB, clone every other wip-tagged repo,
        restore the latest reachable backup) or just launch the app.

    git and the .NET SDK are the only two tools that must already exist on PATH to get this far
    (this script tries winget first before giving up); everything else bootstrap needs is
    installed from inside Launcher itself.

    Safe to re-run: questions default sensibly, and nothing destructive ever
    overwrites an existing settings.json or shortcut target.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File setup.ps1
#>
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repo      = $PSScriptRoot
$deployPs1 = Join-Path $repo 'run.ps1'
$slnx      = Join-Path $repo 'MindAttic.Launcher.slnx'
$deployDir = 'C:\Apps\MindAttic.Launcher'
$exeName   = 'MindAttic.Launcher.exe'

function Write-Step([string]$text) { Write-Host "`n== $text ==" -ForegroundColor Cyan }
function Write-Info([string]$text) { Write-Host "   $text" -ForegroundColor DarkGray }
function Write-Warn2([string]$text) { Write-Host "   $text" -ForegroundColor Yellow }
function Write-Ok([string]$text) { Write-Host "   $text" -ForegroundColor Green }

function Read-WithDefault([string]$prompt, [string]$default) {
    $answer = Read-Host "$prompt [$default]"
    if ([string]::IsNullOrWhiteSpace($answer)) { return $default }
    return $answer.Trim()
}

function Read-YesNo([string]$prompt, [bool]$defaultYes) {
    $suffix = if ($defaultYes) { 'Y/n' } else { 'y/N' }
    while ($true) {
        $answer = (Read-Host "$prompt [$suffix]").Trim()
        if ($answer -eq '') { return $defaultYes }
        if ($answer -match '^(y|yes)$') { return $true }
        if ($answer -match '^(n|no)$') { return $false }
        Write-Warn2 "Please answer y or n."
    }
}

Write-Host "MindAttic.Launcher setup" -ForegroundColor Magenta
Write-Info "This checkout: $repo"

if (-not (Test-Path $deployPs1) -or -not (Test-Path $slnx)) {
    Write-Host "This doesn't look like the MindAttic.Launcher repo root (run.ps1 / MindAttic.Launcher.slnx not found next to setup.ps1)." -ForegroundColor Red
    Write-Host "Run setup.ps1 from inside the cloned checkout." -ForegroundColor Red
    exit 1
}

# ── Prerequisites ────────────────────────────────────────────────────────────
Write-Step "Checking prerequisites"

function Test-OnPath([string]$cmd) {
    return [bool](Get-Command $cmd -ErrorAction SilentlyContinue)
}

function Install-ViaWingetIfMissing([string]$cmd, [string]$wingetId, [string]$label) {
    if (Test-OnPath $cmd) { Write-Ok "$label found"; return $true }
    if (-not (Test-OnPath 'winget')) { return $false }

    Write-Warn2 "$label not found - installing via winget ($wingetId)..."
    winget install --id $wingetId -e --silent --accept-package-agreements --accept-source-agreements | Out-Null
    # winget can need a PATH refresh mid-process; re-read it from the registry
    # rather than requiring a new shell.
    $machine = [Environment]::GetEnvironmentVariable('Path', 'Machine')
    $user    = [Environment]::GetEnvironmentVariable('Path', 'User')
    $env:Path = "$machine;$user"
    if (Test-OnPath $cmd) { Write-Ok "$label installed"; return $true }
    return $false
}

# git and the .NET SDK are the one chicken-and-egg step that can't be MindAttic.Launcher's own
# code (see Commands/BootstrapCommand.cs's doc comment): you need both to get this checkout built
# at all. Try winget before failing, same silent-install posture as the rest of the pipeline.
$hasDotnet = Install-ViaWingetIfMissing 'dotnet' 'Microsoft.DotNet.SDK.10' '.NET SDK'
$hasGit    = Install-ViaWingetIfMissing 'git' 'Git.Git' 'git'
$hasWt     = Test-OnPath 'wt'

if ($hasWt)     { Write-Ok "Windows Terminal (wt) found" } else { Write-Warn2 "wt not found on PATH - agent tabs won't be able to open until Windows Terminal is installed." }
foreach ($tool in 'robocopy', 'sqlcmd') {
    if (-not (Test-OnPath $tool)) { Write-Warn2 "$tool not found - the 'bootstrap' command (after setup) installs it." }
}

if (-not $hasDotnet -or -not $hasGit) {
    Write-Host "`nCouldn't auto-install the missing tool(s) above (no winget, or the install failed)." -ForegroundColor Red
    Write-Host "Install them manually, then re-run setup.ps1." -ForegroundColor Red
    exit 1
}

# ── Question 1: workspace directory ─────────────────────────────────────────
Write-Step "Workspace directory"
Write-Info "This is the folder that holds this checkout and every sibling MindAttic-style"
Write-Info "project repo you'll manage from the Launcher."

$parentActual = Split-Path -Parent $repo
$workspaceRoot = Read-WithDefault "Workspace directory" $parentActual

$workspaceFull = [System.IO.Path]::GetFullPath($workspaceRoot)
$parentFull    = [System.IO.Path]::GetFullPath($parentActual)

if ($workspaceFull -ne $parentFull) {
    Write-Warn2 "This checkout actually lives under: $parentFull"
    Write-Warn2 "The Launcher always treats a repo's parent folder as the workspace root - that can't be changed from here."
    if (-not (Read-YesNo "Move this checkout to `"$workspaceFull`" yourself later and re-run setup, or continue anyway with $parentFull as the workspace root" $false)) {
        Write-Host "Move the checkout under $workspaceFull and re-run setup.ps1." -ForegroundColor Yellow
        exit 1
    }
    $workspaceFull = $parentFull
}

New-Item -ItemType Directory -Force -Path $workspaceFull | Out-Null
Write-Ok "Workspace directory: $workspaceFull"

# ── Question 2: desktop icon ────────────────────────────────────────────────
Write-Step "Desktop icon"
$wantsIcon = Read-YesNo "Create a desktop shortcut to MindAttic.Launcher" $true

# ── Question 3: example repo ────────────────────────────────────────────────
Write-Step "Example project"
Write-Info "Point at one existing git repo (anywhere on this machine) to seed the"
Write-Info "Launcher's roster, so Open Project Tab has something to open right away."
Write-Info "Leave blank to skip - you can add projects later from the app's Discover Projects menu."

$exampleProject = $null
while ($true) {
    $candidate = (Read-Host "Example repo path (blank to skip)").Trim('"').Trim()
    if ($candidate -eq '') { break }

    if (-not [System.IO.Path]::IsPathRooted($candidate)) {
        $candidate = Join-Path $workspaceFull $candidate
    }
    $candidateFull = [System.IO.Path]::GetFullPath($candidate)
    $dotGit = Join-Path $candidateFull '.git'

    if (-not (Test-Path $candidateFull -PathType Container) -or -not (Test-Path $dotGit)) {
        Write-Warn2 "No git repo found at $candidateFull - try again, or leave blank to skip."
        continue
    }

    $name = Split-Path -Leaf $candidateFull.TrimEnd('\', '/')
    $repoUrl = $null
    if ($hasGit) {
        try {
            $url = (& git -C $candidateFull remote get-url origin 2>$null)
            if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($url)) { $repoUrl = $url.Trim() }
        } catch { }
    }

    $description = Read-WithDefault "Short description" ""
    if ($description -eq '') { $description = $null }

    $palette = @('#00BFA5', '#E91E63', '#00CED1', '#FFA726', '#7E57C2', '#26A69A',
                 '#EF5350', '#42A5F5', '#8D6E63', '#26C6DA', '#D4E157', '#AB47BC')
    $hash = [Math]::Abs($name.GetHashCode())
    $suggestedColor = $palette[$hash % $palette.Length]
    $tabColor = Read-WithDefault "Tab color (hex)" $suggestedColor

    $exampleProject = [ordered]@{
        name        = $name
        repo        = $name
        repoUrl     = $repoUrl
        path        = $candidateFull
        description = $description
        openWith    = $null
        runCommand  = $null
        tabAlias    = $null
        tabColor    = $tabColor
        colorScheme = "MindAttic-$name"
        sqlServer   = $null
        databases   = @()
    }
    Write-Ok "Will register '$name' at $candidateFull"
    break
}

# ── Question 4: GitHub token ─────────────────────────────────────────────────
Write-Step "GitHub token (optional)"
Write-Info "A personal access token lets Launcher read your repos' topics - a repo tagged"
Write-Info "'wip' on GitHub shows up in the roster. Give one now and this script tags every"
Write-Info "repo it can see as 'wip' for you, so the roster is full on the very first launch"
Write-Info "instead of tagging each repo by hand on GitHub first."
Write-Info "Leave blank to skip - you can set this later from the app's Settings menu."

$githubToken = (Read-Host "GitHub token (blank to skip)").Trim()

# ── Seed settings (only if nothing real exists yet) ─────────────────────────
Write-Step "Settings"

$vaultSettingsPath = Join-Path $env:APPDATA 'MindAttic\MindAttic.Launcher\settings.json'
if (Test-Path $vaultSettingsPath) {
    Write-Info "Existing settings already found at $vaultSettingsPath - leaving them untouched."
    if ($exampleProject) {
        Write-Info "Add '$($exampleProject.name)' yourself from the app's Discover Projects menu, or delete that file and re-run setup.ps1 to reseed."
    }
}
else {
    $wtPackaged   = Join-Path $env:LOCALAPPDATA 'Packages\Microsoft.WindowsTerminal_8wekyb3d8bbwe\LocalState\settings.json'
    $wtUnpackaged = Join-Path $env:LOCALAPPDATA 'Microsoft\Windows Terminal\settings.json'
    $wtPath = $null
    if (Test-Path $wtPackaged) { $wtPath = $wtPackaged }
    elseif (Test-Path $wtUnpackaged) { $wtPath = $wtUnpackaged }
    else { Write-Warn2 "Couldn't find Windows Terminal's settings.json - project tab colors won't be written until you set windowsTerminalSettingsPath yourself." }

    $seed = [ordered]@{
        windowsTerminalSettingsPath = $wtPath
        projects                    = @()
    }
    if ($exampleProject) { $seed.projects = @($exampleProject) }

    $seedPath = Join-Path $repo 'settings.json'
    $seed | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $seedPath -Encoding UTF8
    Write-Ok "Wrote seed settings to $seedPath (picked up automatically on first run)"
}

# ── Deploy ───────────────────────────────────────────────────────────────────
Write-Step "Building and deploying"
& $deployPs1 -Force
if ($LASTEXITCODE -ne 0) {
    Write-Host "`nDeploy failed - fix the build errors above, then re-run setup.ps1." -ForegroundColor Red
    exit 1
}

$exe = Join-Path $deployDir $exeName
$bat = Join-Path $deployDir 'run.bat'

# ── Adopt repos as 'wip' (only if a GitHub token was given) ─────────────────
if ($githubToken) {
    Write-Step "Tagging your repos as 'wip'"
    # Passed via env var, not a CLI arg, so the token doesn't land in process-list logging.
    $env:MINDATTIC_GITHUB_TOKEN = $githubToken
    try {
        & $exe adopt-repos
    }
    finally {
        Remove-Item Env:\MINDATTIC_GITHUB_TOKEN -ErrorAction SilentlyContinue
    }
}

# ── Desktop shortcut ─────────────────────────────────────────────────────────
if ($wantsIcon) {
    Write-Step "Desktop shortcut"
    try {
        $desktop = [Environment]::GetFolderPath('Desktop')
        $lnkPath = Join-Path $desktop 'MindAttic.Launcher.lnk'
        $shell = New-Object -ComObject WScript.Shell
        $shortcut = $shell.CreateShortcut($lnkPath)
        $shortcut.TargetPath = $bat
        $shortcut.WorkingDirectory = $deployDir
        $shortcut.IconLocation = "$exe,0"
        $shortcut.Description = 'MindAttic.Launcher'
        $shortcut.Save()
        Write-Ok "Shortcut created at $lnkPath"
    }
    catch {
        Write-Warn2 "Couldn't create the desktop shortcut: $($_.Exception.Message)"
        Write-Info "You can still launch from $bat"
    }
}

# ── Done ─────────────────────────────────────────────────────────────────────
Write-Step "Done"
Write-Ok "Deployed to $deployDir"
if ($exampleProject) { Write-Ok "Roster seeded with '$($exampleProject.name)'" }

if (Read-YesNo "Run new-machine bootstrap now (install sqlcmd/LocalDB, clone every other tagged repo, restore the latest reachable backup)" $true) {
    & $exe bootstrap --workspace-root $workspaceFull
}
elseif (Read-YesNo "Launch MindAttic.Launcher now" $true) {
    & $exe
}
