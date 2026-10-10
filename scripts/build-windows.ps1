<#
.SYNOPSIS
  Restores, builds, tests and publishes Nexora as a self-contained win-x64 build.

.DESCRIPTION
  Run from any directory on Windows with the .NET SDK pinned in global.json installed.
  The installer step is added in Phase 9; this script stops after publishing.

.PARAMETER Configuration
  Build configuration. Default: Release.

.PARAMETER SkipTests
  Skip the test run (not recommended for release builds).
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',
    [switch] $SkipTests
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $root 'Nexora.sln'
$publishDir = Join-Path $root 'artifacts/publish/win-x64'
$testResults = Join-Path $root 'artifacts/test-results'

function Invoke-Step([string] $Name, [scriptblock] $Action) {
    Write-Host "==> $Name"
    & $Action
    if ($LASTEXITCODE -ne 0) { throw "$Name failed with exit code $LASTEXITCODE." }
}

Invoke-Step 'Restore' { dotnet restore $solution --locked-mode }
Invoke-Step 'Build' { dotnet build $solution --configuration $Configuration --no-restore }

if (-not $SkipTests) {
    Invoke-Step 'Test' {
        dotnet test (Join-Path $root 'tests/Nexora.Tests/Nexora.Tests.csproj') --configuration $Configuration --no-build `
            --logger 'trx;LogFileName=results.trx' --results-directory $testResults
    }
}

Invoke-Step 'Publish win-x64' {
    dotnet publish (Join-Path $root 'src/Nexora.Desktop/Nexora.Desktop.csproj') --configuration $Configuration `
        --runtime win-x64 --self-contained true --output $publishDir
}

Write-Host "Published to $publishDir"
Write-Host 'Installer packaging is not implemented yet (Phase 9).'
