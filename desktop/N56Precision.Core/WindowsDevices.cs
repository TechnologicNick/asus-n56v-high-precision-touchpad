using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace N56Precision.Backend;

internal static class Win32
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern SafeFileHandle CreateFileW(string path, uint access, uint sharing, nint security, uint disposition, uint flags, nint template);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool DeviceIoControl(SafeFileHandle device, uint code, byte[] input, uint inputSize, [Out] byte[]? output, uint outputSize, out uint returned, nint overlapped);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern bool WaitNamedPipeW(string path, uint timeout);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool PeekNamedPipe(SafeFileHandle pipe, nint buffer, uint size, nint read, out uint available, nint left);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool ReadFile(SafeFileHandle file, [Out] byte[] buffer, uint size, out uint read, nint overlapped);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool WriteFile(SafeFileHandle file, byte[] buffer, uint size, out uint written, nint overlapped);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern SafeWaitHandle OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern uint WaitForSingleObject(SafeWaitHandle handle, uint milliseconds);
    [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll", SetLastError = true)] internal static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern nint SendMessageTimeoutW(nint window, uint message, nuint param, string setting, uint flags, uint timeout, out nuint result);
    [StructLayout(LayoutKind.Sequential)] internal struct Mouse { public int X, Y; public uint Data, Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Explicit, Size = 32)] internal struct Payload { [FieldOffset(0)] public Mouse Mouse; }
    [StructLayout(LayoutKind.Sequential)] internal struct Input { public uint Type; public Payload Payload; }
    internal static void Check(bool result) { if (!result) throw new Win32Exception(Marshal.GetLastWin32Error()); }
}

public sealed class IoctlDevice : IDisposable
{
    private readonly SafeFileHandle handle;
    public IoctlDevice(string path)
    {
        handle = Win32.CreateFileW(path, 0xC0000000, 3, 0, 3, 0, 0);
        if (handle.IsInvalid) { int error = Marshal.GetLastWin32Error(); handle.Dispose(); throw new Win32Exception(error, $"Cannot open {path}: {new Win32Exception(error).Message}"); }
    }
    public byte[] Ioctl(uint code, byte[] input, int outputSize = 0)
    {
        var output = outputSize > 0 ? new byte[outputSize] : null;
        Win32.Check(Win32.DeviceIoControl(handle, code, input, (uint)input.Length, output, (uint)outputSize, out uint returned, 0));
        return output == null ? Array.Empty<byte>() : output[..(int)returned];
    }
    public string Status()
    {
        var bytes = Ioctl(0x00226004, Array.Empty<byte>(), 96);
        if (bytes.Length != 96) throw new IOException("Unsupported bridge diagnostics layout");
        uint Value(int i) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(i * 4));
        return $"mode={Value(1)} surface={(Value(2) & 1) != 0} waiting-for-lift={Value(3) != 0} submitted={Value(5)} blocked={Value(6)} last-status=0x{Value(7):X8} capabilities={Value(10)} certification={Value(11)} mode-requests={Value(18)} switch-requests={Value(19)}";
    }
    public void Dispose() => handle.Dispose();
}

public sealed class AsusSettings : IDisposable
{
    public const int Size = 0x314;
    private readonly IoctlDevice device;
    private byte[]? original;
    public AsusSettings() { device = new IoctlDevice(@"\\.\AsusTP"); }
    public byte[] Read()
    {
        var request = new byte[Size]; BinaryPrimitives.WriteUInt32LittleEndian(request.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(request.AsSpan(12), Size);
        var result = device.Ioctl(0x221594, request, Size);
        if (result.Length != Size || Enumerable.Range(0, 17).Any(i => BinaryPrimitives.ReadUInt32LittleEndian(result.AsSpan(16 + i * 4)) > 1))
            throw new IOException("Unsupported ASUS settings layout; nothing has been changed.");
        return result;
    }
    public static byte[] GestureRequest(byte[] source, IReadOnlyDictionary<int, uint> values)
    {
        if (source.Length != Size || values.Any(p => p.Key is < 1 or > 14 || p.Value > 1)) throw new ArgumentException("Invalid ASUS settings");
        var request = (byte[])source.Clone(); BinaryPrimitives.WriteUInt32LittleEndian(request, 1);
        foreach (var (index, value) in values) BinaryPrimitives.WriteUInt32LittleEndian(request.AsSpan(16 + index * 4), value);
        return request;
    }
    public void Apply(Settings config, bool zoneMuted = false)
    {
        original ??= Read(); device.Ioctl(0x221594, GestureRequest(original, GestureOwnership.AsusValues(config, zoneMuted)));
    }
    public void Restore()
    {
        if (original == null) return;
        var request = (byte[])original.Clone(); BinaryPrimitives.WriteUInt32LittleEndian(request, 1);
        device.Ioctl(0x221594, request); original = null;
    }
    public void Dispose() { try { Restore(); } finally { device.Dispose(); } }
}

public sealed class ParentProcess : IDisposable
{
    private readonly SafeWaitHandle handle;
    public ParentProcess(int pid)
    {
        handle = Win32.OpenProcess(0x100000, false, pid);
        if (handle.IsInvalid) { int error = Marshal.GetLastWin32Error(); handle.Dispose(); throw new Win32Exception(error); }
    }
    public bool Exited()
    {
        uint result = Win32.WaitForSingleObject(handle, 0);
        if (result == uint.MaxValue) throw new Win32Exception(Marshal.GetLastWin32Error());
        return result == 0;
    }
    public void Dispose() => handle.Dispose();
}

public sealed class MouseOutput
{
    public bool ButtonsDown() => (Win32.GetAsyncKeyState(1) & 0x8000) != 0 || (Win32.GetAsyncKeyState(2) & 0x8000) != 0;
    public void RightClick()
    {
        var inputs = new Win32.Input[2]; inputs[0].Payload.Mouse.Flags = 8; inputs[1].Payload.Mouse.Flags = 16;
        int size = Marshal.SizeOf<Win32.Input>();
        if (size != 40) throw new PlatformNotSupportedException("Only x64 Windows is supported");
        uint sent = Win32.SendInput(2, inputs, size);
        if (sent != 2)
        {
            int error = Marshal.GetLastWin32Error();
            if (sent == 1) Win32.SendInput(1, new[] { inputs[1] }, size);
            throw new Win32Exception(error, "Could not send the matched right-click pair");
        }
    }
    public static void SuppressOrdinaryTwoFingerTap()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\PrecisionTouchPad");
        key.SetValue("TwoFingerTapEnabled", 0, RegistryValueKind.DWord);
        // Windows 10 may still need the real Settings checkbox changed once to refresh its cache.
        Win32.SendMessageTimeoutW(0xffff, 0x1a, 0, "PrecisionTouchPad", 2, 1000, out _);
    }
}
