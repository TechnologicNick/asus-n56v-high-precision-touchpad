param([string]$PackagePath, [switch]$DeveloperPreview, [switch]$Elevated)
$ErrorActionPreference = 'Stop'
$package = [IO.Path]::GetFullPath($PackagePath)
foreach ($name in @('N56PrecisionBridge.inf','N56PrecisionBridge.sys','N56PrecisionBridge.cat')) {
    if (!(Test-Path -LiteralPath (Join-Path $package $name))) { throw "Missing driver package file: $name" }
}
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (!$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    if ($Elevated) { throw 'Administrator access was not granted.' }
    $script = $PSCommandPath.Replace("'", "''"); $quotedPackage = $package.Replace("'", "''")
    $command = "& '$script' -PackagePath '$quotedPackage' -Elevated" + $(if ($DeveloperPreview) { ' -DeveloperPreview' } else { '' })
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($command))
    $helper = Start-Process powershell.exe -Verb RunAs -WindowStyle Hidden -ArgumentList '-NoProfile','-ExecutionPolicy','Bypass','-EncodedCommand',$encoded -PassThru
    $helper.WaitForExit()
    if ($helper.ExitCode) { throw 'Driver setup failed. See %ProgramData%\N56PrecisionBridge\driver-install.log.' }
    exit
}
$folder = Join-Path $env:ProgramData 'N56PrecisionBridge'; New-Item -ItemType Directory -Force -Path $folder | Out-Null
Start-Transcript -Path (Join-Path $folder 'driver-install.log') -Append | Out-Null
try {
    if (Get-Process N56Precision -ErrorAction SilentlyContinue) { throw 'Exit N56 Precision before changing the virtual driver.' }
    Add-Type -Path (Join-Path $PSScriptRoot 'DriverAccess.cs')
    $catalog = Get-AuthenticodeSignature -LiteralPath (Join-Path $package 'N56PrecisionBridge.cat')
    $development = $catalog.SignerCertificate.Subject -notmatch 'CN=Microsoft Windows Hardware Compatibility Publisher'
    if ($development) {
        if (!$DeveloperPreview) { throw 'This is a test-signed driver. Explicit -DeveloperPreview consent is required; see the security warning in docs/release.md.' }
        if (![N56DriverAccess]::TestSigningActive()) { throw 'Windows Test Mode is not active. No boot/security setting was changed. Follow docs/development.md, reboot yourself, then retry.' }
        if (!$catalog.SignerCertificate -or $catalog.SignerCertificate.Subject -ne 'CN=N56 Precision Bridge Development') { throw 'Unexpected development certificate.' }
        # Explicit preview setup trusts only the certificate embedded in this catalog.
        $certificateFile = Join-Path $folder 'preview-signer.cer'
        Export-Certificate -Cert $catalog.SignerCertificate -FilePath $certificateFile -Force | Out-Null
        foreach ($store in @('Cert:\LocalMachine\Root','Cert:\LocalMachine\TrustedPublisher')) {
            Import-Certificate -FilePath $certificateFile -CertStoreLocation $store | Out-Null
        }
        $catalog = Get-AuthenticodeSignature -LiteralPath (Join-Path $package 'N56PrecisionBridge.cat')
    }
    if ($catalog.Status -ne 'Valid') { throw 'Driver catalog signature validation failed.' }
    $devices = @(Get-CimInstance Win32_PnPEntity | Where-Object { @($_.HardwareID) -contains 'ROOT\N56PrecisionBridge' })
    if ($devices.Count -gt 1) { throw 'Multiple bridge root devices exist; resolve duplicates first.' }
    $instance = if ($devices.Count) { $devices[0].PNPDeviceID } else { [N56DriverAccess]::CreateRootDevice() }
    & pnputil.exe /add-driver (Join-Path $package 'N56PrecisionBridge.inf') /install
    if ($LASTEXITCODE -notin @(0,3010)) { throw 'PnP driver installation failed. The log includes the root instance for manual recovery.' }
    "Root device: $instance" | Write-Host
    $device = Get-CimInstance Win32_PnPEntity | Where-Object PNPDeviceID -eq $instance
    if ($device.ConfigManagerErrorCode -ne 0) { throw "Virtual device problem $($device.ConfigManagerErrorCode); a reboot may be required." }
    [pscustomobject]@{ instance=$instance; development=$development; signer=$catalog.SignerCertificate.Thumbprint } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $folder 'release-driver.json') -Encoding UTF8
} finally { Stop-Transcript | Out-Null }
