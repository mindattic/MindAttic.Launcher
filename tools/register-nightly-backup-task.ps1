#Requires -Version 5.1
<#
.SYNOPSIS
    One-time installer: registers the MindAtticLauncherNightlyBackup scheduled task, which runs
    the deployed MindAttic.Launcher.exe's own `backup` command silently every night.

.DESCRIPTION
    Supersedes MindAttic.Bob's MindAtticBobNightlyBackup: the action here is the real, tested
    `backup --keep <N>` command (see Commands/BackupCommand.cs) instead of a separate PowerShell
    payload script - the same four backup services the interactive Backup menu uses, and its own
    retention pruning, with no extra wrapper or log file (it already logs through MindAttic.Log).

    Registers a Windows Scheduled Task that:
      - Fires daily at -Time (default 00:00 / midnight).
      - Runs completely silently - LogonType S4U means it runs in a non-interactive session with
        no window at all, even if nobody is logged in (no stored password required, unlike "run
        whether logged on or not").
      - Catches up via -StartWhenAvailable if the machine was off/asleep at the trigger time, and
        retries a few times on failure.
      - Lives under \MindAttic\Launcher\, per MindAttic.HouseRules.md#HOUSE-LAW-11 - every
        MindAttic scheduled task lives under \MindAttic\<Project>\, never the Task Scheduler root.

.EXAMPLE
    .\tools\register-nightly-backup-task.ps1
    .\tools\register-nightly-backup-task.ps1 -Time "23:30" -Keep 14

.NOTES
    To remove: Unregister-ScheduledTask -TaskName MindAtticLauncherNightlyBackup -TaskPath '\MindAttic\Launcher\'
#>
param(
    [string]$TaskName = 'MindAtticLauncherNightlyBackup',
    [string]$TaskPath = '\MindAttic\Launcher\',
    [string]$Time     = '00:00',
    [int]$Keep        = 30,
    [string]$ExePath  = 'C:\Apps\MindAttic.Launcher\MindAttic.Launcher.exe'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Test-Path $ExePath)) { throw "MindAttic.Launcher.exe not found at '$ExePath' - deploy it first (deploy.ps1)." }

$Action = New-ScheduledTaskAction -Execute $ExePath -Argument "backup --keep $Keep"
$Trigger   = New-ScheduledTaskTrigger -Daily -At $Time
$Principal = New-ScheduledTaskPrincipal -UserId $env:USERNAME -LogonType S4U
$Settings  = New-ScheduledTaskSettingsSet -StartWhenAvailable -Hidden `
    -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 5)

try {
    # The ScheduledTasks module's CDXML cmdlets don't reliably honor the session-wide
    # $ErrorActionPreference = 'Stop' above (a known quirk) - pass -ErrorAction Stop
    # explicitly so a real failure (e.g. Access Denied when not elevated) actually
    # halts the script instead of silently falling through to the "registered"
    # success message below.
    Register-ScheduledTask -TaskName $TaskName -TaskPath $TaskPath -Action $Action -Trigger $Trigger `
        -Principal $Principal -Settings $Settings -Force -ErrorAction Stop `
        -Description 'Nightly MindAttic.Launcher backup (files, AppData, SQL Server, SQLite) to R:\Backup\MindAttic (rolling retention via -Keep).' `
        | Out-Null
}
catch {
    throw "Register-ScheduledTask failed: $($_.Exception.Message) - LogonType S4U needs an elevated (Administrator) PowerShell window."
}

# A stale copy at the old root-level path (pre-dating the \MindAttic\<Project>\ folder
# convention) would otherwise run alongside this one and back up twice a night.
$stale = Get-ScheduledTask -TaskName $TaskName -TaskPath '\' -ErrorAction SilentlyContinue
if ($stale) {
    Unregister-ScheduledTask -TaskName $TaskName -TaskPath '\' -Confirm:$false -ErrorAction Stop
    Write-Host "Removed stale root-level copy of '$TaskName'." -ForegroundColor Yellow
}

Write-Host "Scheduled task '$TaskPath$TaskName' registered - runs daily at $Time, keeping the newest $Keep backup(s)." -ForegroundColor Green
Write-Host "Run it now with: Start-ScheduledTask -TaskName $TaskName -TaskPath '$TaskPath'" -ForegroundColor Cyan
