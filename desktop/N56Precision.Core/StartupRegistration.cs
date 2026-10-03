using Microsoft.Win32;

namespace N56Precision;

public static class StartupRegistration
{
    public const string ValueName = "N56Precision";
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public static string Command(string executable) => "\"" + Path.GetFullPath(executable) + "\" --tray";
    public static bool IsEnabled(string executable)
    {
        if (!OperatingSystem.IsWindows()) return false;
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        return string.Equals(key?.GetValue(ValueName) as string, Command(executable), StringComparison.OrdinalIgnoreCase);
    }
    public static void SetEnabled(string executable, bool enabled)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
        if (enabled) key.SetValue(ValueName, Command(executable), RegistryValueKind.String);
        else key.DeleteValue(ValueName, false);
    }
}
