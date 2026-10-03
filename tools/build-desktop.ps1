param([string]$Configuration = 'Release', [string]$OutputDirectory = '')
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (!$OutputDirectory) { $OutputDirectory = Join-Path $projectRoot 'artifacts\desktop' }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$vswhere = 'C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe'
$instances = (& $vswhere -all -format json | ConvertFrom-Json)
$vs = $instances | Select-Object -First 1
if (!$vs) { throw 'Visual Studio with Windows desktop build tools is required.' }
$msbuild = Join-Path $vs.installationPath 'MSBuild\Current\Bin\amd64\MSBuild.exe'
$env:PATH = 'C:\Program Files\dotnet;' + $env:PATH
$priTasks = Join-Path $vs.installationPath 'MSBuild\Microsoft\VisualStudio\v17.0\AppxPackage\Microsoft.Build.Packaging.Pri.Tasks.dll'
if (!(Test-Path -LiteralPath $priTasks)) { throw 'Visual Studio Windows application packaging tools are not installed yet.' }
& $msbuild (Join-Path $projectRoot 'desktop\N56Precision\N56Precision.csproj') /restore /t:Publish /p:Configuration=$Configuration /p:Platform=x64 "/p:PublishDir=$OutputDirectory\" /v:minimal /nologo
if ($LASTEXITCODE) { throw 'WinUI desktop build failed.' }
& 'C:\Program Files\dotnet\dotnet.exe' publish (Join-Path $projectRoot 'desktop\N56Precision.Worker\N56Precision.Worker.csproj') -c Release -r win-x64 --self-contained true -o (Join-Path $OutputDirectory 'worker') --nologo
if ($LASTEXITCODE) { throw 'C# worker publish failed.' }
Write-Host "Launch $OutputDirectory\N56Precision.exe (no administrator prompt)."
