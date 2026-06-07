#requires -Version 5.1
<#
    codex.ps1 - the MindAttic Codex doctor + digest CLI for MindAttic.Console (CODE: MCO).
    No build step. Runs under Windows PowerShell 5.1 and PowerShell 7+.

    Usage:
      powershell -File tools/codex.ps1 doctor    # validate the canon; exit non-zero on hard error
      powershell -File tools/codex.ps1 digest    # regenerate docs/BIBLE.digest.md from BIBLE.md
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('doctor', 'digest')]
    [string]$Command = 'doctor'
)

$ErrorActionPreference = 'Stop'

$RepoRoot   = Split-Path -Parent $PSScriptRoot
$DocsDir    = Join-Path $RepoRoot 'docs'
$BiblePath  = Join-Path $DocsDir  'BIBLE.md'
$StoriesPath= Join-Path $DocsDir  'USER_STORIES.md'
$AmendPath  = Join-Path $DocsDir  'AMENDMENTS.md'
$RfcDir     = Join-Path $DocsDir  'rfc'
$DataDir    = Join-Path $DocsDir  'data'
$DigestPath = Join-Path $DocsDir  'BIBLE.digest.md'
$TestsDir   = Join-Path $RepoRoot 'MindAttic.Console.Tests'

# Status glyphs built from code points so this .ps1 stays pure ASCII on disk
# (Windows PowerShell 5.1 reads scripts as the ANSI codepage, mangling raw UTF-8 emoji).
$script:GlyphDone    = [char]::ConvertFromUtf32(0x2705)   # white heavy check mark
$script:GlyphPartial = [char]::ConvertFromUtf32(0x1F7E1)  # yellow circle
$script:GlyphPlanned = [char]::ConvertFromUtf32(0x2B1C)   # white large square
$script:GlyphCut     = [char]::ConvertFromUtf32(0x1F5D1)  # wastebasket

$script:Errors   = New-Object System.Collections.Generic.List[string]
$script:Warnings = New-Object System.Collections.Generic.List[string]
function Add-Err ($m)  { $script:Errors.Add($m) }
function Add-Warn ($m) { $script:Warnings.Add($m) }

# Canon docs are UTF-8. Windows PowerShell 5.1's default Get-Content uses the ANSI codepage,
# which mangles multi-byte chars (the section sign, em-dashes, status emoji). Decode UTF-8
# explicitly so anchor/glyph matching and the digest are byte-accurate.
function Read-Utf8Raw ($path)   { [System.IO.File]::ReadAllText($path, [System.Text.UTF8Encoding]::new($false)) }
function Read-Utf8Lines ($path) { [System.IO.File]::ReadAllLines($path, [System.Text.UTF8Encoding]::new($false)) }

function Get-FrontMatter ($path) {
    # Returns a hashtable of the top YAML front-matter block, or $null if absent.
    $lines = Read-Utf8Lines $path
    if ($lines.Count -eq 0 -or $lines[0].Trim() -ne '---') { return $null }
    $fm = @{}
    for ($i = 1; $i -lt $lines.Count; $i++) {
        if ($lines[$i].Trim() -eq '---') { return $fm }
        if ($lines[$i] -match '^\s*([A-Za-z0-9_]+)\s*:\s*(.*?)\s*$') {
            $fm[$Matches[1]] = $Matches[2]
        }
    }
    return $null  # no closing fence
}

function Test-FrontMatter ($path, $expectedLayer) {
    $rel = $path.Substring($RepoRoot.Length).TrimStart('\','/')
    $fm = Get-FrontMatter $path
    if ($null -eq $fm) { Add-Err "front-matter: $rel has no valid closed '---' YAML block"; return }
    foreach ($k in 'codex','project','code','layer','status','updated') {
        if (-not $fm.ContainsKey($k)) { Add-Err "front-matter: $rel missing key '$k'" }
    }
    if ($fm.ContainsKey('codex') -and $fm['codex'] -ne '1') { Add-Err "front-matter: $rel codex must be 1" }
    if ($fm.ContainsKey('code')  -and $fm['code'] -ne 'MCO') { Add-Err "front-matter: $rel code must be MCO" }
    if ($expectedLayer -and $fm.ContainsKey('layer') -and $fm['layer'] -ne $expectedLayer) {
        Add-Err "front-matter: $rel layer is '$($fm['layer'])', expected '$expectedLayer'"
    }
    if ($fm.ContainsKey('updated') -and $fm['updated'] -notmatch '^\d{4}-\d{2}-\d{2}$') {
        Add-Err "front-matter: $rel updated '$($fm['updated'])' is not YYYY-MM-DD"
    }
}

# ---- collect all canon files ---------------------------------------------------
function Get-CanonFiles {
    $files = @()
    if (Test-Path $BiblePath)   { $files += [pscustomobject]@{ Path = $BiblePath;   Layer = 'bible' } }
    if (Test-Path $StoriesPath) { $files += [pscustomobject]@{ Path = $StoriesPath; Layer = 'stories' } }
    if (Test-Path $AmendPath)   { $files += [pscustomobject]@{ Path = $AmendPath;   Layer = 'amendments' } }
    if (Test-Path $RfcDir) {
        Get-ChildItem -LiteralPath $RfcDir -Filter '*.md' -File | ForEach-Object {
            $files += [pscustomobject]@{ Path = $_.FullName; Layer = 'rfc' }
        }
    }
    if (Test-Path $DataDir) {
        Get-ChildItem -LiteralPath $DataDir -Filter '*.json' -File -Recurse |
            Where-Object { $_.FullName -notmatch '[\\/]_schema[\\/]' } | ForEach-Object {
            $files += [pscustomobject]@{ Path = $_.FullName; Layer = 'data' }
        }
    }
    return $files
}

# ================================ DIGEST ========================================
function Get-Section ($text, $anchor) {
    # Return the body of a "## ... {#anchor}" section up to the next "## ".
    $pattern = '(?ms)^##[^\r\n]*\{#' + [regex]::Escape($anchor) + '\}[^\r\n]*\r?\n(.*?)(?=^##\s|\Z)'
    $m = [regex]::Match($text, $pattern)
    if ($m.Success) { return $m.Groups[1].Value.Trim() }
    return ''
}

function Invoke-Digest {
    if (-not (Test-Path $BiblePath)) { Add-Err "digest: $BiblePath not found"; return }
    $bible = Read-Utf8Raw $BiblePath

    $sect = [char]::ConvertFromUtf32(0x00A7)   # section sign used in {#MCO-<sect>N} anchors
    $s1 = Get-Section $bible ('MCO-' + $sect + '1')
    $s3 = Get-Section $bible ('MCO-' + $sect + '3')
    $s5 = Get-Section $bible ('MCO-' + $sect + '5')
    $s9 = Get-Section $bible ('MCO-' + $sect + '9')

    # status index from USER_STORIES.md
    $done = 0; $partial = 0; $planned = 0; $cut = 0
    if (Test-Path $StoriesPath) {
        $st = Read-Utf8Raw $StoriesPath
        $done    = ([regex]::Matches($st, $script:GlyphDone)).Count
        $partial = ([regex]::Matches($st, $script:GlyphPartial)).Count
        $planned = ([regex]::Matches($st, $script:GlyphPlanned)).Count
        $cut     = ([regex]::Matches($st, $script:GlyphCut)).Count
    }

    # latest amendment head (first "## " heading in AMENDMENTS.md)
    $amendHead = ''
    if (Test-Path $AmendPath) {
        $am = Read-Utf8Lines $AmendPath
        $last = $null
        foreach ($l in $am) { if ($l -match '^##\s+(.*)$') { $last = $Matches[1] } }
        if ($last) { $amendHead = $last }
    }

    $sb = New-Object System.Text.StringBuilder
    [void]$sb.AppendLine('AUTHORITATIVE - full detail in docs/BIBLE.md')
    [void]$sb.AppendLine('<!-- generatedFrom: MCO-bible -->')
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine('# MindAttic.Console (MCO) - Bible digest')
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine("## The one sentence (#MCO-${sect}1)")
    [void]$sb.AppendLine($s1); [void]$sb.AppendLine('')
    [void]$sb.AppendLine("## What it is NOT (#MCO-${sect}3)")
    [void]$sb.AppendLine($s3); [void]$sb.AppendLine('')
    [void]$sb.AppendLine("## The Laws (#MCO-${sect}5)")
    [void]$sb.AppendLine($s5); [void]$sb.AppendLine('')
    [void]$sb.AppendLine("## Glossary (#MCO-${sect}9)")
    [void]$sb.AppendLine($s9); [void]$sb.AppendLine('')
    [void]$sb.AppendLine('## Status index (docs/USER_STORIES.md)')
    [void]$sb.AppendLine("- done: $done")
    [void]$sb.AppendLine("- partial: $partial")
    [void]$sb.AppendLine("- planned: $planned")
    [void]$sb.AppendLine("- cut: $cut")
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine('## Latest amendment')
    [void]$sb.AppendLine($amendHead)

    Set-Content -LiteralPath $DigestPath -Value $sb.ToString() -Encoding UTF8
    Write-Host "digest: wrote $($DigestPath.Substring($RepoRoot.Length).TrimStart('\','/')) (done=$done partial=$partial planned=$planned cut=$cut)"
}

# ================================ DOCTOR ========================================
function Invoke-Doctor {
    $files = Get-CanonFiles
    if ($files.Count -eq 0) { Add-Err 'doctor: no canon files found under docs/' }

    # 1) front-matter
    foreach ($f in $files) { Test-FrontMatter $f.Path $f.Layer }

    # 2) collect anchors + cross-refs across all canon docs
    $anchors    = @{}   # id -> count
    $linkRefs   = New-Object System.Collections.Generic.List[object]   # @{ id; src }
    foreach ($f in $files | Where-Object { $_.Layer -ne 'data' }) {
        $text = Read-Utf8Raw $f.Path
        $rel  = $f.Path.Substring($RepoRoot.Length).TrimStart('\','/')
        foreach ($m in [regex]::Matches($text, '\{#([^}]+)\}')) {
            $id = $m.Groups[1].Value
            if ($anchors.ContainsKey($id)) { $anchors[$id]++ } else { $anchors[$id] = 1 }
        }
        # markdown links to #anchors: [text](path#anchor) or (#anchor)
        foreach ($m in [regex]::Matches($text, '\]\(([^)]*#[^)]+)\)')) {
            $target = $m.Groups[1].Value
            $hash = $target.Substring($target.IndexOf('#') + 1)
            if ($hash) { $linkRefs.Add([pscustomobject]@{ Id = $hash; Src = $rel }) }
        }
    }

    # 2a) unique anchors
    foreach ($id in $anchors.Keys) {
        if ($anchors[$id] -gt 1) { Add-Err "ids: anchor '{#$id}' defined $($anchors[$id]) times (must be unique)" }
    }

    # 2b) resolve cross-refs. Anchors defined in HouseRules (HOUSE-*) live outside this repo;
    #     accept them as external. Plain markdown heading slugs (no CODE prefix) are GitHub
    #     auto-anchors and are not Codex IDs, so only check {#...}-style IDs (contain '-' + caps).
    foreach ($ref in $linkRefs) {
        $id = $ref.Id
        if ($id -like 'HOUSE-*') { continue }                 # external (MindAttic.HouseRules.md)
        if ($id -notmatch '^MCO-') { continue }               # not a Codex ID we own
        if (-not $anchors.ContainsKey($id)) {
            Add-Err "links: $($ref.Src) references '#$id' but no '{#$id}' anchor exists"
        }
    }

    # 3) data files validate against schema (only if any exist)
    if (Test-Path $DataDir) {
        $dataFiles = Get-ChildItem -LiteralPath $DataDir -Filter '*.json' -File -Recurse |
            Where-Object { $_.FullName -notmatch '[\\/]_schema[\\/]' }
        $ids = @{}
        foreach ($df in $dataFiles) {
            $schema = Join-Path (Join-Path $DataDir '_schema') ($df.BaseName + '.schema.json')
            if (-not (Test-Path $schema)) { Add-Err "data: $($df.Name) has no _schema/$($df.BaseName).schema.json" }
            try {
                $json = Get-Content -LiteralPath $df.FullName -Raw | ConvertFrom-Json
                foreach ($e in @($json)) {
                    if ($e.PSObject.Properties.Name -contains 'id') {
                        if ($ids.ContainsKey($e.id)) { Add-Err "data: duplicate entity id '$($e.id)'" } else { $ids[$e.id] = $true }
                    }
                }
            } catch { Add-Err "data: $($df.Name) is not valid JSON: $($_.Exception.Message)" }
        }
    }

    # 4) every done story names a test token, and (best-effort) the test exists
    if (Test-Path $StoriesPath) {
        $storyText = Read-Utf8Raw $StoriesPath
        $testIndex = ''
        if (Test-Path $TestsDir) {
            $testIndex = (Get-ChildItem -LiteralPath $TestsDir -Filter '*.cs' -File -Recurse |
                Get-Content -Raw) -join "`n"
        }
        # split into story bullets, look at each done-glyph line block
        $donePattern = '(?ms)^\-\s+\*\*(MCO-US-[A-Za-z0-9]+)\s*' + $script:GlyphDone + '\*\*.*?(?=^\-\s+\*\*MCO-US-|\Z)'
        foreach ($m in [regex]::Matches($storyText, $donePattern)) {
            $sid   = $m.Groups[1].Value
            $block = $m.Value
            $tokens = [regex]::Matches($block, '`([A-Za-z_][A-Za-z0-9_]*)`') |
                ForEach-Object { $_.Groups[1].Value } |
                Where-Object { $_ -match '_' }   # test method names are snake_case here
            if ($tokens.Count -eq 0) {
                Add-Err "stories: $sid is done but names no test token"
            } elseif ($testIndex) {
                foreach ($t in ($tokens | Select-Object -Unique)) {
                    if ($testIndex -notmatch [regex]::Escape($t)) {
                        Add-Warn "stories: $sid cites test '$t' not found in the test tree"
                    }
                }
            }
        }
    }

    # 5) code paths cited in the bible exist on disk. The bible cites source paths relative to
    #    the main project dir (e.g. Models/Project.cs lives at MindAttic.Console/Models/Project.cs),
    #    so resolve against the repo root, the main project dir, and the test dir.
    if (Test-Path $BiblePath) {
        $bibleText = Read-Utf8Raw $BiblePath
        $bases = @($RepoRoot, (Join-Path $RepoRoot 'MindAttic.Console'), $TestsDir)
        $seen = @{}
        foreach ($m in [regex]::Matches($bibleText, '`([A-Za-z0-9_./\\-]+\.(?:cs|csproj|ps1|json|slnx))`')) {
            $p = $m.Groups[1].Value
            if ($p -match '[\\/]' ) {  # only path-like citations, not bare filenames
                if ($seen.ContainsKey($p)) { continue }
                $seen[$p] = $true
                $rel = $p -replace '/','\'
                $found = $false
                foreach ($b in $bases) { if (Test-Path (Join-Path $b $rel)) { $found = $true; break } }
                if (-not $found) { Add-Err "bible: cited path '$p' not found under repo root, project dir, or tests" }
            }
        }
    }

    # 6) digest freshness: regenerate, then check source mtime <= artifact mtime
    Invoke-Digest
    if ((Test-Path $DigestPath) -and (Test-Path $BiblePath)) {
        $bibleMtime  = (Get-Item $BiblePath).LastWriteTimeUtc
        $digestMtime = (Get-Item $DigestPath).LastWriteTimeUtc
        if ($bibleMtime -gt $digestMtime) {
            Add-Warn 'digest: BIBLE.md is newer than BIBLE.digest.md (regenerated just now)'
        }
        # generatedFrom presence
        $dg = Read-Utf8Raw $DigestPath
        if ($dg -notmatch 'generatedFrom:\s*MCO-bible') {
            Add-Err 'digest: BIBLE.digest.md missing generatedFrom marker'
        }
    }

    # ---- report ----
    Write-Host ''
    Write-Host 'codex doctor - MindAttic.Console (MCO)'
    Write-Host '--------------------------------------'
    $checks = @(
        'front-matter on every canon file',
        'unique {#...} anchors',
        'cross-ref links resolve',
        'data files validate (n/a if none)',
        'every done story names an existing test',
        'cited bible paths exist',
        'digest regenerated + fresh'
    )
    foreach ($c in $checks) { Write-Host "  [check] $c" }
    Write-Host ''
    if ($script:Warnings.Count -gt 0) {
        Write-Host "WARNINGS ($($script:Warnings.Count)):"
        foreach ($w in $script:Warnings) { Write-Host "  ! $w" }
        Write-Host ''
    }
    if ($script:Errors.Count -gt 0) {
        Write-Host "FAIL - $($script:Errors.Count) hard error(s):"
        foreach ($e in $script:Errors) { Write-Host "  X $e" }
        exit 1
    }
    Write-Host 'PASS - no hard errors.'
    exit 0
}

switch ($Command) {
    'digest' { Invoke-Digest }
    'doctor' { Invoke-Doctor }
}
