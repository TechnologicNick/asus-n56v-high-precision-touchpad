param([switch]$Elevated)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$statePath = Join-Path $projectRoot 'artifacts\installation\state.json'
if (!(Test-Path -LiteralPath $statePath)) { throw 'No installation state is recorded for this project.' }
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (!$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    if ($Elevated) { throw 'Administrator access was not granted.' }
    $scriptLiteral = $PSCommandPath.Replace("'", "''")
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes("& '$scriptLiteral' -Elevated"))
    Start-Process -FilePath 'powershell.exe' -Verb RunAs -WindowStyle Hidden -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-EncodedCommand',$encoded) | Out-Null
    Write-Host 'Administrator rollback helper started. See artifacts\installation\remove.log.'
    exit 0
}
Start-Transcript -Path (Join-Path $projectRoot 'artifacts\installation\remove.log') -Append | Out-Null
try {
    $state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
    $devices = @(Get-CimInstance Win32_PnPEntity | Where-Object { @($_.HardwareID) -contains 'ROOT\N56PrecisionBridge' })
    if ($devices.Count -gt 1) { throw 'Multiple virtual devices found; remove them manually in Device Manager.' }
    if ($devices.Count) {
        if ($devices[0].PNPDeviceID -notmatch '^ROOT\\(SYSTEM|N56PRECISIONBRIDGE)\\[0-9A-F]+$') { throw 'Unexpected virtual device instance ID.' }
        $devcon = Join-Path $projectRoot '.deps\wdk-fast\c\tools\10.0.26100.0\x64\devcon.exe'
        & $devcon remove "@$($devices[0].PNPDeviceID)"
        if ($LASTEXITCODE -gt 1) { throw 'Virtual device removal failed; certificate trust was retained.' }
    }
    if ($state.certificateThumbprint) {
        if ($state.certificateThumbprint -notmatch '^[0-9A-F]{40}$') { throw 'Invalid saved certificate thumbprint.' }
        foreach ($store in @('Root','TrustedPublisher','My')) {
            $path = "Cert:\LocalMachine\$store\$($state.certificateThumbprint)"
            if (Test-Path -LiteralPath $path) {
                $cert = Get-Item -LiteralPath $path
                if ($cert.Subject -ne 'CN=N56 Precision Bridge Development') { throw 'Certificate subject does not match this project.' }
                Remove-Item -LiteralPath $path
            }
        }
    }
    if ($state.enabledTestMode -and !$state.originalTestMode) {
        & bcdedit.exe /set testsigning off
        if ($LASTEXITCODE) { throw 'Could not disable test signing.' }
        Write-Host 'Test signing disabled for the next boot. Restart Windows to leave Test Mode.'
    }
    $state.stage = 'removed'
    $state | ConvertTo-Json | Set-Content -LiteralPath $statePath -Encoding UTF8
    Write-Host 'Virtual device and project signing certificate removed. The ASUS driver is retained. Driver Store files may remain and can be removed through Device Manager.'
} finally {
    Stop-Transcript | Out-Null
}
