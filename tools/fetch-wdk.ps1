$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$deps = Join-Path $projectRoot '.deps'
New-Item -ItemType Directory -Force $deps | Out-Null
$zip = Join-Path $deps 'wdk-fast.zip'
$destination = Join-Path $deps 'wdk-fast'
$expectedSha512 = '5aHxX9Jh7tv9wqfXR2taMxP0LOwz1t/2UwAx70Z3uyKlkkYmNOxCTQ3rod1mShsJJ63e6Nmq/53gPJkcLOQIAg=='
if (!(Test-Path -LiteralPath $zip)) {
    & curl.exe -L --fail --silent --show-error 'https://api.nuget.org/v3-flatcontainer/microsoft.windows.wdk.x64/10.0.26100.1/microsoft.windows.wdk.x64.10.0.26100.1.nupkg' -o $zip
    if ($LASTEXITCODE) { throw 'WDK download failed.' }
}
$hashHex = (Get-FileHash -LiteralPath $zip -Algorithm SHA512).Hash
$hashBytes = New-Object byte[] 64
for ($i = 0; $i -lt 64; $i++) { $hashBytes[$i] = [Convert]::ToByte($hashHex.Substring($i * 2, 2), 16) }
if ([Convert]::ToBase64String($hashBytes) -ne $expectedSha512) { throw 'WDK package hash mismatch.' }
if (!(Test-Path -LiteralPath (Join-Path $destination 'c\Lib\10.0.26100.0\km\x64\vhfkm.lib'))) {
    Expand-Archive -LiteralPath $zip -DestinationPath $destination -Force
}
Write-Host "Verified local WDK: $destination"
