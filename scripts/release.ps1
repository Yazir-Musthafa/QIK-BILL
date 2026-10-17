<#
.SYNOPSIS
  Builds a QIK Suite release: tested self-contained build, optional code signing, Inno Setup installer and checksums.

.DESCRIPTION
  Steps:
    1. scripts/build-windows.ps1 (locked restore, build, tests, win-x64 publish) with the release version.
    2. Code signing of QIKSuite.exe and the QIK Suite (Nexora.*) assemblies, when a certificate is supplied.
    3. The installer, compiled with Inno Setup 6 (ISCC.exe) from installer/QIKSuite.iss.
    4. Signing of the installer, when a certificate is supplied.
    5. SHA256SUMS.txt beside the installer.

  Code signing: pass -CertificatePath and -CertificatePassword, or set QIK_SIGN_PFX (path to a .pfx) and
  QIK_SIGN_PASSWORD. Without a certificate the release is built unsigned and the script says so. signtool.exe comes
  with the Windows SDK; the script looks for it on PATH and in the usual Windows Kits folders.

.EXAMPLE
  ./scripts/release.ps1 -Version 1.0.0
  ./scripts/release.ps1 -Version 1.0.0 -CertificatePath C:\certs\qik.pfx -CertificatePassword (Read-Host -AsSecureString)
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [ValidatePattern('^\d+\.\d+\.\d+$')] [string] $Version,
    [string] $CertificatePath = $env:QIK_SIGN_PFX,
    [object] $CertificatePassword = $env:QIK_SIGN_PASSWORD,
    [string] $TimestampUrl = 'http://timestamp.digicert.com',
    [switch] $SkipTests
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $root 'artifacts/publish/win-x64'
$installerDir = Join-Path $root 'artifacts/installer'
function Invoke-Step([string] $Name, [scriptblock] $Action) {
    Write-Host "==> $Name"
    & $Action
    if ($LASTEXITCODE -ne 0) { throw "$Name failed with exit code $LASTEXITCODE." }
}

function Find-Tool([string] $Name, [string[]] $Candidates) {
    $onPath = Get-Command $Name -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }
    foreach ($pattern in $Candidates) {
        $found = Get-ChildItem -Path $pattern -ErrorAction SilentlyContinue | Sort-Object FullName -Descending | Select-Object -First 1
        if ($found) { return $found.FullName }
    }
    return $null
}

# 1. Build, test and publish.
$buildArgs = @{ Configuration = 'Release'; Version = $Version }
if ($SkipTests) { $buildArgs.SkipTests = $true }
& (Join-Path $PSScriptRoot 'build-windows.ps1') @buildArgs

# 2 and 4. Code signing (optional until a certificate is supplied).
$sign = $false
if ($CertificatePath) {
    if (-not (Test-Path $CertificatePath)) { throw "Certificate not found: $CertificatePath" }
    $signtool = Find-Tool 'signtool.exe' @("${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe")
    if (-not $signtool) { throw 'signtool.exe was not found. Install the Windows SDK or add signtool to PATH.' }
    $plainPassword = if ($CertificatePassword -is [securestring]) {
        [Runtime.InteropServices.Marshal]::PtrToStringBSTR([Runtime.InteropServices.Marshal]::SecureStringToBSTR($CertificatePassword))
    } else { [string] $CertificatePassword }
    $sign = $true
} else {
    Write-Warning 'No code-signing certificate supplied: the build and installer are NOT signed. Windows SmartScreen will warn users.'
}

function Invoke-Sign([string[]] $Files) {
    if (-not $sign) { return }
    Invoke-Step "Sign $($Files.Count) file(s)" {
        & $signtool sign /fd SHA256 /td SHA256 /tr $TimestampUrl /f $CertificatePath /p $plainPassword @Files
    }
}

Invoke-Sign (@(Join-Path $publishDir 'QIKSuite.exe') + (Get-ChildItem $publishDir -Filter 'Nexora.*.dll' | ForEach-Object FullName))

# 3. Installer.
$iscc = Find-Tool 'ISCC.exe' @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe", "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe")
if (-not $iscc) { throw 'Inno Setup 6 (ISCC.exe) was not found. Install it from https://jrsoftware.org/isinfo.php or with "choco install innosetup".' }
if (Test-Path $installerDir) { Remove-Item $installerDir -Recurse -Force }
Invoke-Step 'Installer' {
    & $iscc "/DAppVersion=$Version" "/DSourceDir=$publishDir" "/DOutputDir=$installerDir" (Join-Path $root 'installer/QIKSuite.iss')
}
$installer = Join-Path $installerDir "QIKSuite-Setup-$Version.exe"
if (-not (Test-Path $installer)) { throw "The installer was not produced: $installer" }
Invoke-Sign @($installer)

# 5. Checksums.
$hash = (Get-FileHash $installer -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $(Split-Path $installer -Leaf)" | Set-Content (Join-Path $installerDir 'SHA256SUMS.txt') -Encoding ascii
Write-Host "Release $Version ready: $installer"
Write-Host "SHA-256 $hash"
if (-not $sign) { Write-Host 'Unsigned build.' }
