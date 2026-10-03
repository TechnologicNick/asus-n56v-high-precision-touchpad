param([string]$InstallDirectory = (Join-Path $env:LOCALAPPDATA 'Programs\N56Precision'), [switch]$NoStartup, [switch]$DeveloperPreview)
$ErrorActionPreference = 'Stop'
# Run this as the intended user. Only the permission helper elevates.
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$source = Join-Path $root 'App'
if (!(Test-Path -LiteralPath (Join-Path $source 'N56Precision.exe'))) { throw 'Run Install.ps1 from an extracted release package.' }
$target = [IO.Path]::GetFullPath($InstallDirectory).TrimEnd('\')
if ($target -eq [IO.Path]::GetPathRoot($target).TrimEnd('\') -or $target -eq $env:USERPROFILE -or $target -eq $env:LOCALAPPDATA) { throw 'Choose a dedicated application directory.' }
if (Get-Process N56Precision -ErrorAction SilentlyContinue) { throw 'Exit N56 Precision from its tray menu before installing/updating.' }
$devices = @(Get-CimInstance Win32_PnPEntity | Where-Object { @($_.HardwareID) -contains 'ROOT\N56PrecisionBridge' })
if ($devices.Count -gt 1) { throw 'Multiple bridge devices exist; resolve duplicates before setup.' }
if ($devices.Count -eq 0) {
    & (Join-Path $PSScriptRoot 'install-release-driver.ps1') -PackagePath (Join-Path $root 'Driver') -DeveloperPreview:$DeveloperPreview
}
New-Item -ItemType Directory -Force -Path $target | Out-Null
foreach ($item in Get-ChildItem -LiteralPath $source) { Copy-Item -LiteralPath $item.FullName -Destination $target -Recurse -Force }
$support = Join-Path $target 'setup'
New-Item -ItemType Directory -Force -Path $support | Out-Null
foreach ($name in @('set-driver-user-access.ps1','DriverAccess.cs','disable-windows-two-finger-tap.ps1','uninstall-app.ps1')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $support -Force
}
# The worker opens the endpoint with read/write access. Probe before requesting
# UAC so updates do not elevate or restart an already accessible device.
& (Join-Path $target 'worker\N56Precision.Worker.exe') --check-driver
if ($LASTEXITCODE) { & (Join-Path $support 'set-driver-user-access.ps1') }
$exe = Join-Path $target 'N56Precision.exe'
if (!$NoStartup) {
    $runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
    New-Item -Path $runKey -Force | Out-Null
    New-ItemProperty -Path $runKey -Name N56Precision -Value "`"$exe`" --tray" -PropertyType String -Force | Out-Null
}
# Apply the actual Windows setting, including its Windows 10 cache, during explicit setup.
& (Join-Path $support 'disable-windows-two-finger-tap.ps1')
Write-Host "Installed to $target. Starts in the tray at sign-in; settings can disable startup."
Start-Process -FilePath $exe -ArgumentList '--tray' -WindowStyle Hidden
