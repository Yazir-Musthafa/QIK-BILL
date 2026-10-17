<#
.SYNOPSIS
  Restores, builds, tests and publishes QIK Suite (QIKSuite.exe) as a self-contained win-x64 build.

.DESCRIPTION
  Run from any directory on Windows with the .NET SDK pinned in global.json installed.
  scripts/release.ps1 calls this script and then signs and packages the result.

.PARAMETER Configuration
  Build configuration. Default: Release.

.PARAMETER Version
  Version stamped on the assemblies, for example 1.0.0. Default: the version in Directory.Build.props.

.PARAMETER SkipTests
  Skip the test run (not recommended for release builds).
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',
    [string] $Version = '',
    [string] $ActivationUrl = $env:QIK_ACTIVATION_URL,
    [string] $ActivationPublicKey = $env:QIK_ACTIVATION_PUBLIC_KEY,
    [switch] $SkipTests
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $root 'Nexora.sln'
$publishDir = Join-Path $root 'artifacts/publish/win-x64'
$testResults = Join-Path $root 'artifacts/test-results'
$versionArgs = @()
if ($Version) { $versionArgs = @("-p:Version=$Version") }
if ($ActivationUrl) { $versionArgs += "-p:QikActivationUrl=$ActivationUrl" }
if ($ActivationPublicKey) { $versionArgs += "-p:QikActivationPublicKey=$ActivationPublicKey" }

function Invoke-Step([string] $Name, [scriptblock] $Action) {
    Write-Host "==> $Name"
    & $Action
    if ($LASTEXITCODE -ne 0) { throw "$Name failed with exit code $LASTEXITCODE." }
}

Invoke-Step 'Restore' { dotnet restore $solution --locked-mode }
Invoke-Step 'Build' { dotnet build $solution --configuration $Configuration --no-restore @versionArgs }

if (-not $SkipTests) {
    # The working-guide checks read docs/WORKING_GUIDE.md. Checkouts without the docs folder skip only that category.
    $filter = @()
    if (-not (Test-Path (Join-Path $root 'docs/WORKING_GUIDE.md'))) {
        Write-Host 'docs/WORKING_GUIDE.md is not present: skipping the documentation checks (Category=Docs).'
        $filter = @('--filter', 'Category!=Docs')
    }
    Invoke-Step 'Test' {
        dotnet test (Join-Path $root 'tests/Nexora.Tests/Nexora.Tests.csproj') --configuration $Configuration --no-build @filter `
            --logger 'trx;LogFileName=results.trx' --results-directory $testResults
    }
}

if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
Invoke-Step 'Publish win-x64' {
    dotnet publish (Join-Path $root 'src/Nexora.Desktop/Nexora.Desktop.csproj') --configuration $Configuration `
        --runtime win-x64 --self-contained true --output $publishDir @versionArgs
}

Write-Host "Published to $publishDir"
