param([string]$UserSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value, [switch]$Remove, [switch]$Elevated)
$ErrorActionPreference = 'Stop'
# Capture the requested user's SID before elevation, including "over-the-shoulder" UAC.
if ($UserSid -notmatch '^S-1-5-21-(\d+-){3}\d+$') { throw 'Expected an individual local/domain user SID, not a group or well-known account.' }
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (!$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    if ($Elevated) { throw 'Administrator permission was not granted.' }
    $literal = $PSCommandPath.Replace("'", "''")
    $command = "& '$literal' -UserSid '$UserSid' -Elevated" + $(if ($Remove) { ' -Remove' } else { '' })
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($command))
    $helper = Start-Process powershell.exe -Verb RunAs -WindowStyle Hidden -ArgumentList '-NoProfile','-ExecutionPolicy','Bypass','-EncodedCommand',$encoded -PassThru
    $helper.WaitForExit()
    if ($helper.ExitCode) { throw 'Driver permission setup failed. See %ProgramData%\N56PrecisionBridge\access.log.' }
    exit
}
$stateDir = Join-Path $env:ProgramData 'N56PrecisionBridge'
New-Item -ItemType Directory -Path $stateDir -Force | Out-Null
Start-Transcript -Path (Join-Path $stateDir 'access.log') -Append | Out-Null
try {
    if (Get-Process N56Precision -ErrorAction SilentlyContinue) { throw 'Exit N56 Precision before changing driver permissions.' }
    Add-Type -Path (Join-Path $PSScriptRoot 'DriverAccess.cs')
    $devices = @(Get-CimInstance Win32_PnPEntity | Where-Object { @($_.HardwareID) -contains 'ROOT\N56PrecisionBridge' })
    if ($devices.Count -ne 1) { throw 'Expected exactly one installed N56 bridge root device.' }
    $instance = $devices[0].PNPDeviceID
    $statePath = Join-Path $stateDir 'driver-access.json'
    $state = if (Test-Path -LiteralPath $statePath) { Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json } else {
        [pscustomobject]@{ deviceInstance=$instance; previousSddl=[N56DriverAccess]::GetSecurity($instance); userSid=$UserSid }
    }
    if ($state.deviceInstance -ne $instance) { throw 'Saved permissions belong to a different device. Resolve installation state first.' }
    $state | ConvertTo-Json | Set-Content -LiteralPath $statePath -Encoding UTF8
    $sddl = if ($Remove) { if ($state.previousSddl) { $state.previousSddl } else { 'D:P(A;;GA;;;SY)(A;;GA;;;BA)' } } else { "D:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;GRGW;;;$UserSid)" }
    [N56DriverAccess]::SetSecurity($instance, $sddl)
    & pnputil.exe /restart-device $instance
    if ($LASTEXITCODE) { throw 'Device restart failed. Reboot before using the new permissions; no forced reboot was performed.' }
    Write-Host "Driver access updated for $UserSid. No SYSTEM service or global Users/Everyone access was added."
} finally { Stop-Transcript | Out-Null }
