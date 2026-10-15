<#
.SYNOPSIS
  Starts the published QIKSuite.exe on Windows and checks that it gets as far as its first screen.

.DESCRIPTION
  Passes when, within the time limit, the app is still running, has created its database and has logged
  "Workspace initialization completed" with no fatal error. It does not click through any screen. Uses the real
  per-user data folder (%LOCALAPPDATA%\QIKSuite), so run it on a build machine or a fresh account.
#>
[CmdletBinding()]
param(
    [string] $Exe = (Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts/publish/win-x64/QIKSuite.exe'),
    [int] $Seconds = 60
)

$ErrorActionPreference = 'Stop'
$data = Join-Path $env:LOCALAPPDATA 'QIKSuite'
if (-not (Test-Path $Exe)) { throw "Not found: $Exe" }

$process = Start-Process -FilePath $Exe -PassThru
try {
    $deadline = (Get-Date).AddSeconds($Seconds)
    $ready = $false
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 2
        if ($process.HasExited) { throw "QIKSuite.exe exited early with code $($process.ExitCode)." }
        $log = Get-ChildItem (Join-Path $data 'logs') -Filter 'qiksuite-*.jsonl' -ErrorAction SilentlyContinue | Sort-Object LastWriteTime | Select-Object -Last 1
        if ($log) {
            $text = Get-Content $log.FullName -Raw
            if ($text -match '"@l":"Fatal"' -or $text -match 'Startup failed') { throw "Startup logged a fatal error:`n$text" }
            if ($text -match 'Workspace initialization completed' -and (Test-Path (Join-Path $data 'qiksuite.db'))) { $ready = $true; break }
        }
    }
    if (-not $ready) { throw "QIKSuite.exe did not finish starting within $Seconds seconds." }
    Write-Host "Smoke test passed: QIKSuite.exe started, created $data\qiksuite.db and reached its first screen."
} finally {
    if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force }
}
