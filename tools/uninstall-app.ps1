param([switch]$KeepDriverAccess)
$ErrorActionPreference = 'Stop'
if (Get-Process N56Precision -ErrorAction SilentlyContinue) { throw 'Exit N56 Precision from its tray menu before uninstalling.' }
Remove-ItemProperty -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name N56Precision -ErrorAction SilentlyContinue
if (!$KeepDriverAccess) { & (Join-Path $PSScriptRoot 'set-driver-user-access.ps1') -Remove }
Write-Host 'Startup removed and driver permission restored. App files, user preferences and logs are retained; delete them yourself if no longer needed.'
Write-Host 'The original ASUS driver and Windows tap preferences have not been changed. Driver removal/test-signing rollback is a separate administrator operation.'
