#requires -Version 5.1
<#
    SessionStart hook — injects docs/BIBLE.digest.md as authoritative context.
    Emits Claude Code hook JSON on stdout. If the digest is missing/empty, emits {}.
    ASCII-safe: non-ASCII chars are escaped to \uXXXX (Windows PowerShell 5.1 / Win-1252 safe).
#>
$ErrorActionPreference = 'Stop'

# repo root = two levels up from .claude/hooks/
$RepoRoot   = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$DigestPath = Join-Path $RepoRoot 'docs\BIBLE.digest.md'

if (-not (Test-Path $DigestPath)) { Write-Output '{}'; exit 0 }
$digest = Get-Content -LiteralPath $DigestPath -Raw
if ([string]::IsNullOrWhiteSpace($digest)) { Write-Output '{}'; exit 0 }

$preamble = @"
[MindAttic Codex] The following is the AUTHORITATIVE project digest for MindAttic.Launcher (MCO),
generated from docs/BIBLE.md. Treat it as the source of truth for what this project IS, is NOT,
its Laws, and its glossary. Full detail lives in docs/BIBLE.md; amendments in docs/AMENDMENTS.md
win over the bible. Do not contradict it; if reality differs, update the canon and re-run
tools/codex.ps1 doctor.

"@

$context = $preamble + $digest

function ConvertTo-JsonString ([string]$s) {
    $sb = New-Object System.Text.StringBuilder
    [void]$sb.Append('"')
    foreach ($ch in $s.ToCharArray()) {
        $code = [int]$ch
        switch ($ch) {
            '"'  { [void]$sb.Append('\"');  continue }
            '\'  { [void]$sb.Append('\\');  continue }
            "`b" { [void]$sb.Append('\b');  continue }
            "`f" { [void]$sb.Append('\f');  continue }
            "`n" { [void]$sb.Append('\n');  continue }
            "`r" { [void]$sb.Append('\r');  continue }
            "`t" { [void]$sb.Append('\t');  continue }
            default {
                if ($code -lt 32 -or $code -gt 126) {
                    [void]$sb.Append('\u'); [void]$sb.Append($code.ToString('x4'))
                } else {
                    [void]$sb.Append($ch)
                }
            }
        }
    }
    [void]$sb.Append('"')
    return $sb.ToString()
}

$ctxJson = ConvertTo-JsonString $context
$json = '{"hookSpecificOutput":{"hookEventName":"SessionStart","additionalContext":' + $ctxJson + '}}'
Write-Output $json
exit 0
