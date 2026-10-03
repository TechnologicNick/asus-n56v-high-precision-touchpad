param(
    [string]$WdkRoot = (Join-Path $PSScriptRoot '..\.deps\wdk-fast\c'),
    [string]$SdkRoot = 'C:\Program Files (x86)\Windows Kits\10',
    [string]$SdkVersion = '10.0.22621.0'
)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$WdkRoot = [IO.Path]::GetFullPath($WdkRoot)
$vswhere = 'C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe'
$vs = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (!$vs) { throw 'Install the Visual Studio C++ toolchain.' }
$msvc = Get-ChildItem (Join-Path $vs 'VC\Tools\MSVC') -Directory | Sort-Object Name -Descending | Select-Object -First 1
$compiler = Join-Path $msvc.FullName 'bin\Hostx64\x64\cl.exe'
$linker = Join-Path $msvc.FullName 'bin\Hostx64\x64\link.exe'
$out = Join-Path $projectRoot 'artifacts\driver'
New-Item -ItemType Directory -Force $out | Out-Null
$wdkVersion = (Get-ChildItem (Join-Path $WdkRoot 'Include') -Directory | Where-Object Name -like '10.*' | Sort-Object Name -Descending | Select-Object -First 1).Name
if (!$wdkVersion) { throw "No WDK found at $WdkRoot" }
$includes = @(
    (Join-Path $WdkRoot "Include\$wdkVersion\km"),
    (Join-Path $WdkRoot "Include\$wdkVersion\shared"),
    (Join-Path $WdkRoot 'Include\wdf\kmdf\1.15'),
    (Join-Path $SdkRoot "Include\$SdkVersion\shared"),
    (Join-Path $SdkRoot "Include\$SdkVersion\ucrt"),
    (Join-Path $msvc.FullName 'include')
)
$compileArgs = @('/nologo','/c','/W4','/WX','/wd4324','/kernel','/GS','/Zl','/O2',
    '/D_AMD64_','/DAMD64','/DWINVER=0x0A00',
    '/D_WIN32_WINNT=0x0A00','/DNTDDI_VERSION=0x0A000008',
    "/Fo$out\driver.obj", (Join-Path $projectRoot 'driver\driver.c'))
foreach ($include in $includes) { $compileArgs += "/I$include" }
& $compiler @compileArgs
if ($LASTEXITCODE) { throw 'Driver compilation failed.' }
$libs = Join-Path $WdkRoot "Lib\$wdkVersion\km\x64"
$wdfLibs = Join-Path $WdkRoot 'Lib\wdf\kmdf\x64\1.15'
& $linker /nologo /driver /subsystem:native,10.00 /entry:FxDriverEntry /nodefaultlib /machine:x64 /integritycheck /release "/out:$out\N56PrecisionBridge.sys" "$out\driver.obj" "/libpath:$libs" "/libpath:$wdfLibs" ntoskrnl.lib hal.lib wmilib.lib BufferOverflowK.lib wdfldr.lib wdfdriverentry.lib vhfkm.lib
if ($LASTEXITCODE) { throw 'Driver linking failed.' }
Copy-Item -LiteralPath (Join-Path $projectRoot 'driver\N56PrecisionBridge.inf') -Destination $out
$infverif = Join-Path $WdkRoot "tools\$wdkVersion\x64\infverif.exe"
& $infverif /w "$out\N56PrecisionBridge.inf"
if ($LASTEXITCODE) { throw 'INF validation failed.' }
$inf2cat = Join-Path $WdkRoot "bin\$wdkVersion\x86\Inf2Cat.exe"
& $inf2cat "/driver:$out" /os:10_VB_X64,10_NI_X64 /uselocaltime
if ($LASTEXITCODE) { throw 'Driver catalog generation failed.' }
Write-Host "Built unsigned development driver: $out\N56PrecisionBridge.sys"
