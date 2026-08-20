[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$packageRoot = [System.IO.Path]::GetFullPath($PSScriptRoot)
$packageExecutable = Join-Path $packageRoot 'BluetoothOff.exe'
$localAppData = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
$installRoot = [System.IO.Path]::GetFullPath((Join-Path $localAppData 'Programs\BluetoothOff'))
$expectedInstallRoot = [System.IO.Path]::GetFullPath((Join-Path $localAppData 'Programs\BluetoothOff'))
$installedExecutable = Join-Path $installRoot 'BluetoothOff.exe'
$taskName = 'BluetoothOff'
$currentIdentity = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
$currentSid = [System.Security.Principal.WindowsIdentity]::GetCurrent().User.Value

if (-not $installRoot.Equals($expectedInstallRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'Resolved install directory is not the expected per-user Bluetooth Off directory.'
}

if (-not (Test-Path -LiteralPath $packageExecutable -PathType Leaf)) {
    throw 'BluetoothOff.exe must be in the same directory as Install.ps1.'
}

$existingTasks = @(Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue)
if ($existingTasks.Count -gt 1) {
    throw 'More than one scheduled task named BluetoothOff exists. Installation stopped without changing them.'
}

if ($existingTasks.Count -eq 1) {
    $existingTask = $existingTasks[0]
    try {
        $taskPrincipalSid = ([System.Security.Principal.NTAccount] $existingTask.Principal.UserId).Translate(
            [System.Security.Principal.SecurityIdentifier]).Value
    } catch {
        $taskPrincipalSid = $null
    }

    $ownedTask = $existingTask.TaskPath -eq '\' -and
        $existingTask.Actions.Count -eq 1 -and
        [System.IO.Path]::GetFullPath($existingTask.Actions[0].Execute.Trim('"')).Equals(
            $installedExecutable,
            [System.StringComparison]::OrdinalIgnoreCase) -and
        [string]::IsNullOrWhiteSpace($existingTask.Actions[0].Arguments) -and
        $taskPrincipalSid -eq $currentSid

    if (-not $ownedTask) {
        throw 'A scheduled task named BluetoothOff exists but is not owned by this application. Installation stopped without overwriting it.'
    }
}

$runningProcesses = @(Get-Process -Name 'BluetoothOff' -ErrorAction SilentlyContinue | Where-Object {
    try {
        [System.IO.Path]::GetFullPath($_.Path).Equals(
            $installedExecutable,
            [System.StringComparison]::OrdinalIgnoreCase)
    } catch {
        $false
    }
})

if ($runningProcesses.Count -gt 0) {
    $runningProcesses | Stop-Process -Force
    foreach ($runningProcess in $runningProcesses) {
        if (-not $runningProcess.WaitForExit(10000)) {
            throw "Bluetooth Off process $($runningProcess.Id) did not stop in time."
        }
    }
}

New-Item -ItemType Directory -Path $installRoot -Force | Out-Null
Copy-Item -LiteralPath $packageExecutable -Destination $installedExecutable -Force

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

Start-Process -FilePath $installedExecutable -WorkingDirectory $installRoot -WindowStyle Normal

Write-Host "Installed Bluetooth Off to $installRoot"
Write-Host 'A limited per-user scheduled task will start it after sign-in.'
Write-Host 'No Windows Firewall rule was created.'
