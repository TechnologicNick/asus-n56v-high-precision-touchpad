using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

public static class N56DriverAccess
{
    [StructLayout(LayoutKind.Sequential)] struct DeviceInfo
    { public uint Size; public Guid ClassGuid; public uint DevInst; public IntPtr Reserved; }
    [DllImport("setupapi.dll", SetLastError=true)] static extern IntPtr SetupDiCreateDeviceInfoList(IntPtr classGuid, IntPtr parent);
    [DllImport("setupapi.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern bool SetupDiOpenDeviceInfoW(IntPtr set, string instance, IntPtr parent, uint flags, ref DeviceInfo info);
    [DllImport("setupapi.dll", SetLastError=true)] static extern bool SetupDiGetDeviceRegistryPropertyW(IntPtr set, ref DeviceInfo info, uint property, out uint type, byte[] buffer, uint size, out uint required);
    [DllImport("setupapi.dll", SetLastError=true)] static extern bool SetupDiSetDeviceRegistryPropertyW(IntPtr set, ref DeviceInfo info, uint property, byte[] buffer, uint size);
    [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("setupapi.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern bool SetupDiCreateDeviceInfoW(IntPtr set, string name, ref Guid classGuid, string description, IntPtr parent, uint flags, ref DeviceInfo info);
    [DllImport("setupapi.dll", SetLastError=true)] static extern bool SetupDiCallClassInstaller(uint function, IntPtr set, ref DeviceInfo info);
    [DllImport("setupapi.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern bool SetupDiGetDeviceInstanceIdW(IntPtr set, ref DeviceInfo info, StringBuilder instance, uint size, out uint required);
    [StructLayout(LayoutKind.Sequential)] struct IntegrityInfo { public uint Length; public uint Options; }
    [DllImport("ntdll.dll")] static extern int NtQuerySystemInformation(int kind, ref IntegrityInfo info, int size, out int returned);
    public static bool TestSigningActive()
    {
        var info = new IntegrityInfo { Length = 8 }; int returned;
        int result = NtQuerySystemInformation(103, ref info, 8, out returned);
        if (result != 0) throw new InvalidOperationException("Cannot query Code Integrity: " + result);
        return (info.Options & 2) != 0;
    }
    public static string CreateRootDevice()
    {
        var classGuid = new Guid("4d36e97d-e325-11ce-bfc1-08002be10318");
        var memory = Marshal.AllocHGlobal(16);
        IntPtr set = new IntPtr(-1);
        try
        {
            Marshal.StructureToPtr(classGuid, memory, false); set = SetupDiCreateDeviceInfoList(memory, IntPtr.Zero);
            if (set == new IntPtr(-1)) throw new Win32Exception();
            var info = new DeviceInfo { Size = (uint)Marshal.SizeOf(typeof(DeviceInfo)) };
            if (!SetupDiCreateDeviceInfoW(set, "N56PrecisionBridge", ref classGuid, "N56 Precision Gesture Bridge", IntPtr.Zero, 1, ref info)) throw new Win32Exception();
            byte[] ids = Encoding.Unicode.GetBytes("ROOT\\N56PrecisionBridge\0\0");
            if (!SetupDiSetDeviceRegistryPropertyW(set, ref info, 1, ids, (uint)ids.Length)) throw new Win32Exception();
            if (!SetupDiCallClassInstaller(0x19, set, ref info)) throw new Win32Exception();
            var instance = new StringBuilder(1024); uint required;
            if (!SetupDiGetDeviceInstanceIdW(set, ref info, instance, 1024, out required)) throw new Win32Exception();
            return instance.ToString();
        }
        finally { if (set != new IntPtr(-1)) SetupDiDestroyDeviceInfoList(set); Marshal.FreeHGlobal(memory); }
    }
    static IntPtr Open(string instance, out DeviceInfo info)
    {
        info = new DeviceInfo { Size = (uint)Marshal.SizeOf(typeof(DeviceInfo)) };
        var set = SetupDiCreateDeviceInfoList(IntPtr.Zero, IntPtr.Zero);
        if (set == new IntPtr(-1)) throw new Win32Exception();
        if (!SetupDiOpenDeviceInfoW(set, instance, IntPtr.Zero, 0, ref info))
        { var error = new Win32Exception(); SetupDiDestroyDeviceInfoList(set); throw error; }
        var hardware = Read(set, ref info, 1);
        if (hardware == null || !Array.Exists(hardware.Split('\0'), id => string.Equals(id, "ROOT\\N56PrecisionBridge", StringComparison.OrdinalIgnoreCase)))
        { SetupDiDestroyDeviceInfoList(set); throw new InvalidOperationException("Not the N56 bridge hardware ID."); }
        return set;
    }
    static string Read(IntPtr set, ref DeviceInfo info, uint property)
    {
        var buffer = new byte[8192]; uint type, required;
        if (!SetupDiGetDeviceRegistryPropertyW(set, ref info, property, out type, buffer, (uint)buffer.Length, out required))
        { if (Marshal.GetLastWin32Error() == 13) return null; throw new Win32Exception(); }
        return Encoding.Unicode.GetString(buffer, 0, (int)required).TrimEnd('\0');
    }
    public static string GetSecurity(string instance)
    {
        DeviceInfo info; var set = Open(instance, out info);
        try { return Read(set, ref info, 0x18); } finally { SetupDiDestroyDeviceInfoList(set); }
    }
    public static void SetSecurity(string instance, string sddl)
    {
        DeviceInfo info; var set = Open(instance, out info);
        try
        {
            var bytes = Encoding.Unicode.GetBytes(sddl + "\0");
            if (!SetupDiSetDeviceRegistryPropertyW(set, ref info, 0x18, bytes, (uint)bytes.Length)) throw new Win32Exception();
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
    }
}
