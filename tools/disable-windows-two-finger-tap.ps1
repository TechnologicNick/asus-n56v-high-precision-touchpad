$ErrorActionPreference = 'Stop'
# Windows 10 caches this setting: changing the registry alone does not apply it.
# Use the actual settings checkbox without relying on localized text.
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Start-Process 'ms-settings:devices-touchpad'
$condition = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::AutomationIdProperty,
    'SystemSettings_Input_Touch_TwoFingerTapEnabled_CheckBox')
$deadline = [DateTime]::UtcNow.AddSeconds(15)
$checkbox = $null
do {
    $checkbox = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst(
        [System.Windows.Automation.TreeScope]::Descendants, $condition)
    if (!$checkbox) { Start-Sleep -Milliseconds 200 }
} until ($checkbox -or [DateTime]::UtcNow -ge $deadline)
if (!$checkbox) {
    throw 'Windows two-finger tap checkbox was not found. Turn off "Tap with two fingers to right-click" in Windows touchpad settings before starting the bridge.'
}
$toggle = $checkbox.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
if ($toggle.Current.ToggleState -eq [System.Windows.Automation.ToggleState]::On) { $toggle.Toggle() }
if ($toggle.Current.ToggleState -ne [System.Windows.Automation.ToggleState]::Off) {
    throw 'Windows two-finger tap could not be disabled. Check Windows touchpad settings.'
}
Write-Host 'Verified Windows simultaneous two-finger tap is disabled. Hold + tap is handled by the bridge.'
