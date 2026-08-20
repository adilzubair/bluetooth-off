[CmdletBinding(SupportsShouldProcess)]
param(
    [switch] $PurgeData
)

$ErrorActionPreference = 'Stop'

$localAppData = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
$installRoot = [System.IO.Path]::GetFullPath((Join-Path $localAppData 'Programs\BluetoothOff'))
$expectedInstallRoot = [System.IO.Path]::GetFullPath((Join-Path $localAppData 'Programs\BluetoothOff'))
$dataRoot = [System.IO.Path]::GetFullPath((Join-Path $localAppData 'BluetoothOff'))
$expectedDataRoot = [System.IO.Path]::GetFullPath((Join-Path $localAppData 'BluetoothOff'))
$configurationFile = Join-Path $dataRoot 'config.json'
$taskName = 'BluetoothOff'

if (-not $installRoot.Equals($expectedInstallRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'Resolved install directory is not the expected Bluetooth Off directory.'
}

if (-not $dataRoot.Equals($expectedDataRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'Resolved data directory is not the expected Bluetooth Off directory.'
}

function Find-TailscaleExecutable {
    $candidates = @(
        (Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)) 'Tailscale\tailscale.exe'),
        (Join-Path $localAppData 'Tailscale\tailscale.exe')
    )

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return $candidate
        }
    }

    $command = Get-Command tailscale -ErrorAction SilentlyContinue
    return $command.Source
}

function Get-ProxyValues {
    param([object] $Node)

    $values = [System.Collections.Generic.List[string]]::new()
    if ($null -eq $Node) {
        return $values
    }

    if ($Node -is [System.Collections.IEnumerable] -and $Node -isnot [string] -and $Node -isnot [pscustomobject]) {
        foreach ($item in $Node) {
            foreach ($value in (Get-ProxyValues -Node $item)) {
                $values.Add($value)
            }
        }
        return $values
    }

    foreach ($property in $Node.PSObject.Properties) {
        if ($property.Name -eq 'Proxy' -and $property.Value -is [string]) {
            $values.Add($property.Value.TrimEnd('/'))
        } elseif ($property.Value -is [pscustomobject] -or
            ($property.Value -is [System.Collections.IEnumerable] -and $property.Value -isnot [string])) {
            foreach ($value in (Get-ProxyValues -Node $property.Value)) {
                $values.Add($value)
            }
        }
    }

    return $values
}

Get-Process -Name 'BluetoothOff' -ErrorAction SilentlyContinue | Stop-Process -Force

$task = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
if ($null -ne $task -and $PSCmdlet.ShouldProcess($taskName, 'Remove scheduled task')) {
    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
}

if (Test-Path -LiteralPath $configurationFile -PathType Leaf) {
    try {
        $configuration = Get-Content -LiteralPath $configurationFile -Raw | ConvertFrom-Json
        $loopbackPort = [int] $configuration.loopbackPort
        $expectedProxy = "http://127.0.0.1:$loopbackPort"
        $tailscalePath = Find-TailscaleExecutable

        if ($null -ne $tailscalePath) {
            $serveJson = & $tailscalePath serve status --json 2>$null
            if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($serveJson)) {
                $serveState = $serveJson | ConvertFrom-Json
                $proxyValues = @(Get-ProxyValues -Node $serveState)
                if ($proxyValues.Count -eq 1 -and $proxyValues[0] -eq $expectedProxy) {
                    if ($PSCmdlet.ShouldProcess('Tailscale Serve HTTPS port 443', 'Remove Bluetooth Off mapping')) {
                        & $tailscalePath serve --https=443 off
                        if ($LASTEXITCODE -ne 0) {
                            Write-Warning 'Tailscale Serve mapping could not be removed automatically.'
                        }
                    }
                } elseif ($proxyValues.Count -gt 0) {
                    Write-Warning 'Tailscale Serve contains additional or changed routes; it was preserved for safety.'
                }
            }
        }
    } catch {
        Write-Warning 'Tailscale Serve ownership could not be verified; its configuration was preserved.'
    }
}

if ((Test-Path -LiteralPath $installRoot) -and $PSCmdlet.ShouldProcess($installRoot, 'Remove installed application')) {
    Remove-Item -LiteralPath $installRoot -Recurse -Force
}

if ($PurgeData -and (Test-Path -LiteralPath $dataRoot) -and $PSCmdlet.ShouldProcess($dataRoot, 'Permanently remove configuration and logs')) {
    Remove-Item -LiteralPath $dataRoot -Recurse -Force
}

Write-Host 'Bluetooth Off was uninstalled. Tailscale itself was not removed.'
if (-not $PurgeData) {
    Write-Host "Configuration and logs were preserved at $dataRoot"
}
