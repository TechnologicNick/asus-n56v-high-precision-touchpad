param([string]$OutputDirectory = '', [switch]$WithoutDriver)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$version = (Get-Content -LiteralPath (Join-Path $root 'VERSION') -Raw).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+(-[a-z0-9.]+)?$') { throw 'Invalid release version.' }
if (!$OutputDirectory) { $OutputDirectory = Join-Path $root "artifacts\release\N56Precision-$version-win-x64" }
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Release directory already exists. Choose a new output directory; previous artifacts are preserved.' }
New-Item -ItemType Directory -Path $output | Out-Null
& (Join-Path $PSScriptRoot 'test.ps1')
& (Join-Path $PSScriptRoot 'build-desktop.ps1') -OutputDirectory (Join-Path $output 'App')
$tools = Join-Path $output 'tools'; New-Item -ItemType Directory -Path $tools | Out-Null
foreach ($name in @('install-app.ps1','install-release-driver.ps1','uninstall-app.ps1','set-driver-user-access.ps1','DriverAccess.cs','disable-windows-two-finger-tap.ps1')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $tools
}
foreach ($name in @('README.md','LICENSE','VERSION','CHANGELOG.md','THIRD-PARTY-NOTICES.md')) { Copy-Item -LiteralPath (Join-Path $root $name) -Destination $output }
Copy-Item -LiteralPath (Join-Path $root 'release\Install.ps1') -Destination $output
Copy-Item -LiteralPath (Join-Path $root 'docs') -Destination $output -Recurse
Copy-Item -LiteralPath 'C:\Program Files\dotnet\LICENSE.txt' -Destination (Join-Path $output 'DOTNET-LICENSE.txt')
Copy-Item -LiteralPath 'C:\Program Files\dotnet\ThirdPartyNotices.txt' -Destination (Join-Path $output 'DOTNET-THIRD-PARTY-NOTICES.txt')
$appSdk = Join-Path $env:USERPROFILE '.nuget\packages\microsoft.windowsappsdk\1.6.250602001'
Copy-Item -LiteralPath (Join-Path $appSdk 'license.txt') -Destination (Join-Path $output 'WINDOWS-APP-SDK-LICENSE.txt')
Copy-Item -LiteralPath (Join-Path $appSdk 'NOTICE.txt') -Destination (Join-Path $output 'WINDOWS-APP-SDK-NOTICE.txt')
if (!$WithoutDriver) {
    $driver = Join-Path $output 'Driver'; New-Item -ItemType Directory -Path $driver | Out-Null
    foreach ($name in @('N56PrecisionBridge.inf','N56PrecisionBridge.sys','N56PrecisionBridge.cat')) {
        Copy-Item -LiteralPath (Join-Path $root "artifacts\driver\$name") -Destination $driver
    }
    $signature = Get-AuthenticodeSignature -LiteralPath (Join-Path $driver 'N56PrecisionBridge.cat')
    $development = $signature.SignerCertificate.Subject -notmatch 'CN=Microsoft Windows Hardware Compatibility Publisher'
    if ($development -and $version -notmatch '-preview\.') { throw 'A production release requires a Microsoft-signed kernel package; test signatures are never promoted silently.' }
    if ($signature.Status -ne 'Valid') { throw 'Driver catalog signature is not valid on this build machine.' }
}
$manifest = @(Get-ChildItem -LiteralPath $output -File -Recurse | ForEach-Object {
    [pscustomobject]@{ path=$_.FullName.Substring($output.Length + 1).Replace('\','/'); sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
})
$manifest | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $output 'SHA256.json') -Encoding UTF8
$zip = $output + '.zip'
if (Test-Path -LiteralPath $zip) { throw 'Release archive already exists; it was not overwritten.' }
Compress-Archive -LiteralPath $output -DestinationPath $zip -CompressionLevel Optimal
Get-FileHash -LiteralPath $zip -Algorithm SHA256 | Format-List
Write-Host "Created $zip. No ASUS binaries, Python, certificates, private keys, user settings, logs or WDK/VS binaries are packaged."
