<# Downloads and extracts the GitHub artifact internally, then gives the owner one shareable setup EXE. #>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [long] $RunId,
    [string] $OutputDirectory = (Join-Path $HOME 'Downloads')
)
$ErrorActionPreference = 'Stop'
$temporary = Join-Path ([IO.Path]::GetTempPath()) ([Guid]::NewGuid().ToString('N'))
try {
    New-Item -ItemType Directory -Path $temporary | Out-Null
    $artifact = 'qiksuite-installer'
    & gh run download $RunId --repo Yazir-Musthafa/QIK-BILL --name $artifact --dir $temporary
    if ($LASTEXITCODE -ne 0) { throw 'Installer download failed. Check GitHub sign-in, run ID.' }
    $files = @(Get-ChildItem $temporary -Filter 'QIKSuite-Setup-*.exe')
    if ($files.Count -ne 1) { throw 'Expected exactly one setup EXE.' }
    $expected = ((Get-Content (Join-Path $temporary 'SHA256SUMS.txt')) -split '\s+')[0]
    $actual = (Get-FileHash $files[0].FullName -Algorithm SHA256).Hash
    if ($expected -ne $actual) { throw 'Installer checksum did not match.' }
    New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
    $name = $files[0].Name
    $destination = Join-Path $OutputDirectory $name
    Copy-Item $files[0].FullName $destination -Force
    Write-Host "Downloaded: $destination"
    Write-Host 'Share this EXE directly. Customers do not need a ZIP or GitHub access.'
} finally {
    if (Test-Path $temporary) { Remove-Item $temporary -Recurse -Force }
}
