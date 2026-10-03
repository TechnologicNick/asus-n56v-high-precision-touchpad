param([string]$InstallDirectory = (Join-Path $env:LOCALAPPDATA 'Programs\N56Precision'), [switch]$NoStartup, [switch]$DeveloperPreview)
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'tools\install-app.ps1') -InstallDirectory $InstallDirectory -NoStartup:$NoStartup -DeveloperPreview:$DeveloperPreview
