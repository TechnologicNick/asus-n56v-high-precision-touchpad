param([switch]$Elevated)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$stateDir = Join-Path $projectRoot 'artifacts\installation'
New-Item -ItemType Directory -Force $stateDir | Out-Null
$statePath = Join-Path $stateDir 'state.json'
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (!$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    if ($Elevated) { throw 'The elevated process did not receive administrator access.' }
    $scriptLiteral = $PSCommandPath.Replace("'", "''")
    $command = "& '$scriptLiteral' -Elevated"
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($command))
    $process = Start-Process -FilePath 'powershell.exe' -Verb RunAs -WindowStyle Hidden -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-EncodedCommand',$encoded) -PassThru
    Write-Host "Administrator helper started (PID $($process.Id)). See artifacts\installation\install.log and state.json."
    exit 0
}
Start-Transcript -Path (Join-Path $stateDir 'install.log') -Append | Out-Null
try {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class N56CodeIntegrity {
    [StructLayout(LayoutKind.Sequential)] public struct Info { public uint Length; public uint Options; }
    [DllImport("ntdll.dll")] static extern int NtQuerySystemInformation(int kind, ref Info info, int length, out int returned);
    public static uint Options() {
        Info info = new Info { Length = 8 }; int returned;
        int status = NtQuerySystemInformation(103, ref info, 8, out returned);
        if (status != 0) throw new Exception("Cannot query running Code Integrity: " + status);
        return info.Options;
    }
}
'@
    $testMode = ([N56CodeIntegrity]::Options() -band 2) -ne 0
    $state = if (Test-Path -LiteralPath $statePath) { Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json } else {
        [pscustomobject]@{ stage='preparing'; originalTestMode=$testMode; enabledTestMode=$false; certificateThumbprint=''; deviceInstance=''; error='' }
    }
    function Save-State { $state | ConvertTo-Json | Set-Content -LiteralPath $statePath -Encoding UTF8 }
    Save-State
    $package = Join-Path $projectRoot 'artifacts\driver'
    $kit = Join-Path $projectRoot '.deps\wdk-fast\c'
    $signTool = 'C:\Program Files (x86)\Windows Kits\10\bin\10.0.22621.0\x64\signtool.exe'
    $inf2cat = Join-Path $kit 'bin\10.0.26100.0\x86\Inf2Cat.exe'
    $devcon = Join-Path $kit 'tools\10.0.26100.0\x64\devcon.exe'
    foreach ($path in @($signTool,$inf2cat,$devcon,(Join-Path $package 'N56PrecisionBridge.sys'))) {
        if (!(Test-Path -LiteralPath $path)) { throw "Required file missing: $path" }
    }
    $certificate = $null
    if ($state.certificateThumbprint) {
        $certificate = Get-Item -LiteralPath "Cert:\LocalMachine\My\$($state.certificateThumbprint)" -ErrorAction SilentlyContinue
        if (!$certificate -or $certificate.Subject -ne 'CN=N56 Precision Bridge Development') { throw 'Saved signing certificate is missing or has an unexpected subject.' }
    } else {
        $certificate = New-SelfSignedCertificate -Type CodeSigningCert -Subject 'CN=N56 Precision Bridge Development' -FriendlyName 'N56 Precision Bridge development only' -CertStoreLocation 'Cert:\LocalMachine\My' -KeyAlgorithm RSA -KeyLength 2048 -HashAlgorithm SHA256 -NotAfter (Get-Date).AddMonths(3)
        $state.certificateThumbprint = $certificate.Thumbprint
        Save-State
    }
    $publicCert = Join-Path $stateDir 'N56PrecisionBridge.cer'
    Export-Certificate -Cert $certificate -FilePath $publicCert -Force | Out-Null
    foreach ($store in @('Cert:\LocalMachine\Root','Cert:\LocalMachine\TrustedPublisher')) {
        if (!(Test-Path -LiteralPath "$store\$($certificate.Thumbprint)")) {
            Import-Certificate -FilePath $publicCert -CertStoreLocation $store | Out-Null
        }
    }
    & $signTool sign /v /fd SHA256 /sm /s My /sha1 $certificate.Thumbprint (Join-Path $package 'N56PrecisionBridge.sys')
    if ($LASTEXITCODE) { throw 'Driver image signing failed.' }
    # The catalog must be generated AFTER embedding the image signature.
    & $inf2cat "/driver:$package" /os:10_VB_X64,10_NI_X64 /uselocaltime
    if ($LASTEXITCODE) { throw 'Signed image catalog generation failed.' }
    & $signTool sign /v /fd SHA256 /sm /s My /sha1 $certificate.Thumbprint (Join-Path $package 'N56PrecisionBridge.cat')
    if ($LASTEXITCODE) { throw 'Catalog signing failed.' }
    foreach ($file in @('N56PrecisionBridge.sys','N56PrecisionBridge.cat')) {
        & $signTool verify /pa /v (Join-Path $package $file)
        if ($LASTEXITCODE) { throw "Signature verification failed: $file" }
    }
    if (!$testMode) {
        & bcdedit.exe /set testsigning on
        if ($LASTEXITCODE) { throw 'Windows rejected enabling test signing. No device was installed.' }
        $state.enabledTestMode = $true
        $state.stage = 'reboot-required'
        Save-State
        Write-Host 'Package signed. Test signing enabled for the next boot. Restart Windows, then run this script again to install the device.'
    } else {
        $existing = @(Get-CimInstance Win32_PnPEntity | Where-Object { @($_.HardwareID) -contains 'ROOT\N56PrecisionBridge' })
        if ($existing.Count -gt 1) { throw 'Multiple bridge root devices exist; resolve duplicates before installation.' }
        if (!$existing.Count) {
            & $devcon install (Join-Path $package 'N56PrecisionBridge.inf') 'ROOT\N56PrecisionBridge'
            if ($LASTEXITCODE -gt 1) { throw "Device installation failed (devcon $LASTEXITCODE)." }
        } else {
            & $devcon update (Join-Path $package 'N56PrecisionBridge.inf') 'ROOT\N56PrecisionBridge'
            if ($LASTEXITCODE -gt 1) { throw "Device update failed (devcon $LASTEXITCODE)." }
        }
        $device = Get-CimInstance Win32_PnPEntity | Where-Object { @($_.HardwareID) -contains 'ROOT\N56PrecisionBridge' } | Select-Object -First 1
        if (!$device) { throw 'Installation did not create the bridge root device.' }
        $state.deviceInstance = $device.PNPDeviceID
        if ($device.ConfigManagerErrorCode -ne 0) { throw "Bridge device problem code: $($device.ConfigManagerErrorCode)" }
        $worker = Join-Path $projectRoot 'artifacts\desktop\worker\N56Precision.Worker.exe'
        if (Test-Path -LiteralPath $worker) {
            & $worker --check-driver
            if ($LASTEXITCODE) { throw 'The driver loaded but its control interface could not be opened.' }
        }
        $state.stage = 'installed'
        $state.error = ''
        Save-State
        Write-Host 'Virtual driver installed. Grant your account access with tools/set-driver-user-access.ps1, then launch N56Precision.exe normally.'
    }
} catch {
    if ($state) { $state.stage = 'failed'; $state.error = $_.Exception.Message; Save-State }
    Write-Error $_
    exit 1
} finally {
    Stop-Transcript | Out-Null
}
