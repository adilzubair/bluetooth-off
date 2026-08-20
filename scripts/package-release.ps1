[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$')]
    [string] $Version
)

$ErrorActionPreference = 'Stop'

$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts'))
$publishRoot = [System.IO.Path]::GetFullPath((Join-Path $artifactsRoot 'publish'))
$releaseRoot = [System.IO.Path]::GetFullPath((Join-Path $artifactsRoot 'release'))
$stageRoot = [System.IO.Path]::GetFullPath((Join-Path $artifactsRoot 'release-stage'))

foreach ($path in @($publishRoot, $releaseRoot, $stageRoot)) {
    if (-not $path.StartsWith($artifactsRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw 'A release path resolved outside the repository artifacts directory.'
    }
}

foreach ($requiredFile in @('README.md', 'SECURITY.md')) {
    if (-not (Test-Path -LiteralPath (Join-Path $projectRoot $requiredFile) -PathType Leaf)) {
        throw "Required release file $requiredFile is missing."
    }
}

& (Join-Path $PSScriptRoot 'publish.ps1') -Version $Version

foreach ($path in @($releaseRoot, $stageRoot)) {
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Recurse -Force
    }

    New-Item -ItemType Directory -Path $path -Force | Out-Null
}

Copy-Item -LiteralPath (Join-Path $publishRoot 'BluetoothOff.exe') -Destination (Join-Path $stageRoot 'BluetoothOff.exe')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'install-release.ps1') -Destination (Join-Path $stageRoot 'Install.ps1')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'uninstall.ps1') -Destination (Join-Path $stageRoot 'Uninstall.ps1')
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination $stageRoot
Copy-Item -LiteralPath (Join-Path $projectRoot 'SECURITY.md') -Destination $stageRoot
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\INSTALL.md') -Destination $stageRoot
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\APPLE-SHORTCUTS.md') -Destination $stageRoot
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\THREAT-MODEL.md') -Destination $stageRoot
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\SECURITY-REVIEW.md') -Destination $stageRoot

$licensePath = Join-Path $projectRoot 'LICENSE'
if (Test-Path -LiteralPath $licensePath -PathType Leaf) {
    Copy-Item -LiteralPath $licensePath -Destination $stageRoot
}

$portableAsset = Join-Path $releaseRoot 'BluetoothOff-win-x64.exe'
$archiveAsset = Join-Path $releaseRoot 'BluetoothOff-win-x64.zip'
$checksumsAsset = Join-Path $releaseRoot 'SHA256SUMS.txt'

Copy-Item -LiteralPath (Join-Path $stageRoot 'BluetoothOff.exe') -Destination $portableAsset
Compress-Archive -Path (Join-Path $stageRoot '*') -DestinationPath $archiveAsset -CompressionLevel Optimal

$checksumLines = foreach ($asset in @($portableAsset, $archiveAsset)) {
    $hash = Get-FileHash -Algorithm SHA256 -LiteralPath $asset
    "$($hash.Hash.ToLowerInvariant())  $([System.IO.Path]::GetFileName($asset))"
}
$checksumLines | Set-Content -LiteralPath $checksumsAsset -Encoding utf8NoBOM

Write-Host "Release assets for Bluetooth Off $Version are in $releaseRoot"
