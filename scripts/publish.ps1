[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$')]
    [string] $Version = '1.0.0'
)

$ErrorActionPreference = 'Stop'

$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$publishRoot = [System.IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts\publish'))
$allowedArtifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts'))

if (-not $publishRoot.StartsWith($allowedArtifactsRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'Resolved publish directory is outside the repository artifacts directory.'
}

$dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
if ($null -eq $dotnetCommand) {
    $dotnetPath = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)) 'dotnet\dotnet.exe'
    if (-not (Test-Path -LiteralPath $dotnetPath -PathType Leaf)) {
        throw 'The .NET 10 SDK is not installed. Install Microsoft.DotNet.SDK.10 with WinGet.'
    }
} else {
    $dotnetPath = $dotnetCommand.Source
}

if (Test-Path -LiteralPath $publishRoot) {
    Remove-Item -LiteralPath $publishRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $publishRoot -Force | Out-Null

Push-Location $projectRoot
try {
    & $dotnetPath restore 'BluetoothOff.slnx' '--locked-mode' '--configfile' 'NuGet.Config'
    if ($LASTEXITCODE -ne 0) {
        throw 'Locked dependency restore failed.'
    }

    & $dotnetPath test 'BluetoothOff.slnx' '--configuration' 'Release' '--no-restore'
    if ($LASTEXITCODE -ne 0) {
        throw 'Tests failed; publishing was stopped.'
    }

    & $dotnetPath publish 'src\BluetoothOff\BluetoothOff.csproj' `
        '--configuration' 'Release' `
        '--runtime' 'win-x64' `
        '--self-contained' 'true' `
        '--no-restore' `
        '--output' $publishRoot `
        "-p:Version=$Version" `
        '-p:PublishSingleFile=true' `
        '-p:PublishTrimmed=false'
    if ($LASTEXITCODE -ne 0) {
        throw 'Self-contained publish failed.'
    }
} finally {
    Pop-Location
}

$executable = Join-Path $publishRoot 'BluetoothOff.exe'
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw 'Publish completed without producing BluetoothOff.exe.'
}

Write-Host "Published Bluetooth Off $Version to $publishRoot"
