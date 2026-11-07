#Requires -Version 5.1
<#
.SYNOPSIS
    pre-commit hook body (M.LNCH-T116): bumps MindAttic.Launcher.csproj's
    major <Version> by one and stages it, so every commit - including --amend
    - carries its own whole-number version bump (HOUSE-LAW-1). See
    "Whole-number versioning" in README.md for the full story.

.DESCRIPTION
    --amend is deliberately NOT special-cased. A prepare-commit-msg-based
    attempt to skip the bump on --amend/-c/-C (M.LNCH-T117) was tried and
    reverted: git only reports that commit-source classification when no -m
    is also given, so `git commit --amend -m "..."` - the common way people
    actually amend - gets classified as an ordinary "message" commit, same as
    a brand-new one, and would still bump. A detector that silently works for
    one invocation style and not the other is worse than no detector: every
    commit, amends included, bumps by one. Simple, consistent, and easy to
    reason about when debugging ("the version jumped 3? there were 3 commits,
    one of them an amend").
#>
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (& git rev-parse --show-toplevel).Trim()
$proj = Join-Path $repoRoot 'MindAttic.Launcher\MindAttic.Launcher.csproj'

if (-not (Test-Path $proj)) { exit 0 }

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$text = [System.IO.File]::ReadAllText($proj)
if ($text -notmatch '<Version>(\d+)\.\d+\.\d+</Version>') { exit 0 }

$nextMajor = [int]$Matches[1] + 1
$updated = $text -replace '<Version>\d+\.\d+\.\d+</Version>', "<Version>$nextMajor.0.0</Version>"
[System.IO.File]::WriteAllText($proj, $updated, $utf8NoBom)

& git add -- $proj
Write-Host "  Version -> $nextMajor.0.0" -ForegroundColor DarkCyan
exit 0
