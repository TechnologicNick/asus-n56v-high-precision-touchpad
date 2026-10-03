param([string]$AppPath = (Join-Path $PSScriptRoot '..\artifacts\ui-test\N56Precision.exe'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class N56UiTestNative {
 [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
 [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern uint RegisterWindowMessage(string name);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr window);
}
'@
$resolved = [IO.Path]::GetFullPath($AppPath)
if ($resolved -notlike '*\artifacts\ui-test\N56Precision.exe') { throw 'Only the UI-only test build may be launched by this test.' }
$process = Start-Process -FilePath $resolved -ArgumentList '--ui-test' -PassThru
try {
    $deadline = (Get-Date).AddSeconds(15)
    do {
        Start-Sleep -Milliseconds 250
        $process.Refresh()
        if ($process.HasExited) { throw 'UI test app exited during startup.' }
    } while ($process.MainWindowHandle -eq [IntPtr]::Zero -and (Get-Date) -lt $deadline)
    if ($process.MainWindowHandle -eq [IntPtr]::Zero) { throw 'UI test app did not create a window.' }
    $window = $process.MainWindowHandle
    $root = [System.Windows.Automation.AutomationElement]::FromHandle($window)
    function Find-Control([string]$Name) {
        $condition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, $Name)
        $control = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
        if (!$control) { throw "Missing UI control: $Name" }
        return $control
    }
    function Select-Tab([string]$Name) {
        $tab = Find-Control "Tab: $Name"
        $tab.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
        Start-Sleep -Milliseconds 150
    }
    foreach ($name in @('Overview','Windows gestures','ASUS gestures','Buttons & right-click','Diagnostics')) { Find-Control "Tab: $name" | Out-Null }
    Select-Tab 'Buttons & right-click'
    foreach ($name in @('Bottom percentage of touchpad height','Enable hold + tap right-click',
        'Minimum delay between fingers (milliseconds)','Maximum second-finger tap duration (milliseconds)',
        'Tap movement tolerance (millimeters)', 'Hold and tap timeline', 'Hold and tap measurements')) { Find-Control $name | Out-Null }
    Select-Tab 'Windows gestures'
    foreach ($count in 2..5) { Find-Control "Forward $count fingers to Windows" | Out-Null }
    $three = Find-Control 'Forward 3 fingers to Windows'
    $toggle = $three.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
    $original = $toggle.Current.ToggleState
    try {
        $toggle.Toggle()
        Start-Sleep -Milliseconds 100
        if ($toggle.Current.ToggleState -eq $original) { throw 'Forwarding switch did not change.' }
    } finally { if ($toggle.Current.ToggleState -ne $original) { $toggle.Toggle() } }
    [N56UiTestNative]::PostMessage($window, 0x10, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
    Start-Sleep -Milliseconds 250
    if ([N56UiTestNative]::IsWindowVisible($window)) { throw 'Closing the settings window did not hide it.' }
    $show = [N56UiTestNative]::RegisterWindowMessage('N56Precision.ShowSettings.v1')
    [N56UiTestNative]::PostMessage($window, $show, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
    Start-Sleep -Milliseconds 250
    if (![N56UiTestNative]::IsWindowVisible($window)) { throw 'Tray message handler did not reopen the window.' }
    Write-Host 'PASS: sidebar tabs, WinUI controls, switch interaction, close-to-tray, and tray-handler reopen.'
} finally {
    if (!$process.HasExited -and $root) {
        $exit = Find-Control 'Exit UI test'
        $exit.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        if (!$process.WaitForExit(5000)) { throw 'UI test did not shut down gracefully.' }
    }
}
