# Civic Lens Analysis Pipeline - Task Scheduler Setup
#
# This script creates a Windows Task Scheduler job to run the analysis pipeline
# on a regular schedule (default: every 6 hours).
#
# Usage:
#   .\setup-scheduled-task.ps1                    # Install with defaults (every 6 hours)
#   .\setup-scheduled-task.ps1 -RunsPerDay 4      # Run 4 times per day
#   .\setup-scheduled-task.ps1 -Remove            # Remove the scheduled task
#
# Requires: Administrator privileges

param(
    [int]$RunsPerDay = 4,
    [switch]$Remove
)

$TaskName = "CivicLens-AnalysisPipeline"
$ScriptRoot = $PSScriptRoot

if ($Remove) {
    Write-Host "Removing scheduled task: $TaskName"
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction SilentlyContinue
    Write-Host "Task removed."
    exit 0
}

# Calculate interval in hours
$IntervalHours = [math]::Floor(24 / $RunsPerDay)
if ($IntervalHours -lt 1) { $IntervalHours = 1 }

Write-Host "Setting up scheduled task: $TaskName"
Write-Host "  Runs per day: $RunsPerDay"
Write-Host "  Interval: every $IntervalHours hours"

# Build the action
$PwshPath = (Get-Command powershell).Source
$RunScript = Join-Path $ScriptRoot "run.ps1"
$Action = New-ScheduledTaskAction -Execute $PwshPath -Argument "-ExecutionPolicy Bypass -File `"$RunScript`" analyze" -WorkingDirectory $ScriptRoot

# Trigger: repeat every N hours, starting now
$Trigger = New-ScheduledTaskTrigger -Once -At (Get-Date) -RepetitionInterval (New-TimeSpan -Hours $IntervalHours) -RepetitionDuration (New-TimeSpan -Days 365)

# Settings
$Settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable -RunOnlyIfNetworkAvailable

# Principal - run as current user
$Principal = New-ScheduledTaskPrincipal -UserId $env:USERNAME -LogonType Interactive -RunLevel Limited

# Register the task
try {
    # Remove existing task if present
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction SilentlyContinue
    
    Register-ScheduledTask -TaskName $TaskName -Action $Action -Trigger $Trigger -Settings $Settings -Principal $Principal -Description "Runs Civic Lens analysis pipeline (ETL, AI analysis, caching) on a schedule."
    
    Write-Host ""
    Write-Host "Scheduled task created successfully!" -ForegroundColor Green
    Write-Host ""
    Write-Host "The analysis pipeline will run every $IntervalHours hours."
    Write-Host "To run manually: .\run.ps1 analyze"
    Write-Host "To remove: .\setup-scheduled-task.ps1 -Remove"
    Write-Host ""
    Write-Host "View in Task Scheduler: taskschd.msc -> Task Scheduler Library -> $TaskName"
}
catch {
    Write-Host "Failed to create scheduled task: $_" -ForegroundColor Red
    Write-Host ""
    Write-Host "Try running this script as Administrator." -ForegroundColor Yellow
    exit 1
}
