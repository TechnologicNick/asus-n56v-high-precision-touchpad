param([switch]$HardwareSmoke, [string]$WorkerPath = '', [switch]$Ui)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$arguments = @('run','--project',(Join-Path $root 'desktop\N56Precision.Tests'))
if ($HardwareSmoke) {
    if (!$WorkerPath) { $WorkerPath = Join-Path $root 'artifacts\desktop\worker\N56Precision.Worker.exe' }
    $arguments += @('--','--hardware-smoke',[IO.Path]::GetFullPath($WorkerPath))
}
& 'C:\Program Files\dotnet\dotnet.exe' @arguments
if ($LASTEXITCODE) { throw 'C# tests failed.' }
if ($Ui) { & (Join-Path $PSScriptRoot 'test-desktop-ui.ps1') }
