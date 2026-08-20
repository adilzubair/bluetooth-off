[CmdletBinding()]
param(
    [switch] $SkipPublish
)

$ErrorActionPreference = 'Stop'

$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$publishRoot = [System.IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts\publish'))
$localAppData = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
$installRoot = [System.IO.Path]::GetFullPath((Join-Path $localAppData 'Programs\BluetoothOff'))
$expectedInstallRoot = [System.IO.Path]::GetFullPath((Join-Path $localAppData 'Programs\BluetoothOff'))
$taskName = 'BluetoothOff'

if (-not $installRoot.Equals($expectedInstallRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'Resolved install directory is not the expected per-user Bluetooth Off directory.'
}

if (-not $SkipPublish) {
    & (Join-Path $PSScriptRoot 'publish.ps1')
}

$publishedExecutable = Join-Path $publishRoot 'BluetoothOff.exe'
if (-not (Test-Path -LiteralPath $publishedExecutable -PathType Leaf)) {
    throw 'BluetoothOff.exe was not found. Run scripts\publish.ps1 first.'
}

Get-Process -Name 'BluetoothOff' -ErrorAction SilentlyContinue | Stop-Process -Force
New-Item -ItemType Directory -Path $installRoot -Force | Out-Null
Copy-Item -LiteralPath $publishedExecutable -Destination (Join-Path $installRoot 'BluetoothOff.exe') -Force

$installedExecutable = Join-Path $installRoot 'BluetoothOff.exe'
$currentIdentity = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
$action = New-ScheduledTaskAction -Execute $installedExecutable -WorkingDirectory $installRoot
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $currentIdentity
$settings = New-ScheduledTaskSettingsSet `
    -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries `
    -ExecutionTimeLimit ([TimeSpan]::Zero) `
    -RestartCount 3 `
    -RestartInterval (New-TimeSpan -Minutes 1) `
    -StartWhenAvailable
$principal = New-ScheduledTaskPrincipal `
    -UserId $currentIdentity `
    -LogonType Interactive `
    -RunLevel Limited

Register-ScheduledTask `
    -TaskName $taskName `
    -Action $action `
    -Trigger $trigger `
    -Settings $settings `
    -Principal $principal `
    -Description 'Runs the private Bluetooth Off tray application after this user signs in.' `
    -Force | Out-Null

# First launch is intentionally visible because Windows and Tailscale setup require user consent.
Start-Process -FilePath $installedExecutable -WorkingDirectory $installRoot -WindowStyle Normal

Write-Host "Installed Bluetooth Off to $installRoot"
Write-Host 'A limited per-user scheduled task will start it after sign-in.'
Write-Host 'No Windows Firewall rule was created.'
