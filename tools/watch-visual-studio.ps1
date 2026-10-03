param([int]$Minutes = 120)
$vswhere = 'C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe'
for ($check = 0; $check -lt $Minutes; $check++) {
    $instances = (& $vswhere -all -format json | ConvertFrom-Json)
    $complete = @($instances | Where-Object { $_.isComplete -and $_.isLaunchable })
    $dotnet = Test-Path -LiteralPath 'C:\Program Files\dotnet\dotnet.exe'
    $record = [pscustomobject]@{ time=(Get-Date).ToString('o'); complete=($complete.Count -gt 0); dotnet=$dotnet; rebootRequired=(@($instances | Where-Object isRebootRequired).Count -gt 0) }
    $record | ConvertTo-Json -Compress
    if ($record.complete -and $dotnet) { break }
    Start-Sleep -Seconds 60
}
