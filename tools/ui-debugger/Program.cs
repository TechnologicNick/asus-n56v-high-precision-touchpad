using System.Runtime.InteropServices;
using System.Text;

if (args.Length != 1) throw new ArgumentException("Pass the project UI-test executable path.");
string executable = Path.GetFullPath(args[0]);
if (!executable.Contains(@"\artifacts\ui-test\") || !File.Exists(executable)) throw new ArgumentException("Only this project's UI-test executable is allowed.");
var startup = new DebugNative.Startup { Size = Marshal.SizeOf<DebugNative.Startup>() };
if (!DebugNative.CreateProcess(executable, new StringBuilder($"\"{executable}\" --ui-test"), 0, 0, false, 2, 0,
    Path.GetDirectoryName(executable)!, ref startup, out var process)) throw new System.ComponentModel.Win32Exception();
byte[] Read(ulong address, int count)
{
    if (address == 0 || count <= 0 || count > 65536) return [];
    var data = new byte[count];
    return DebugNative.ReadProcessMemory(process.Process, (nint)address, data, (nuint)count, out _) ? data : [];
}
try
{
    DateTime deadline = DateTime.UtcNow.AddSeconds(30);
    while (DateTime.UtcNow < deadline)
    {
        if (!DebugNative.WaitForDebugEvent(out var ev, 1000)) continue;
        uint continuation = 0x10002;
        if (ev.Code == 8)
        {
            var data = Read(ev.Pointer, ev.StringLength * (ev.Unicode != 0 ? 2 : 1));
            Console.WriteLine(ev.Unicode != 0 ? Encoding.Unicode.GetString(data) : Encoding.UTF8.GetString(data));
        }
        if (ev.Code == 1)
        {
            Console.WriteLine($"EXCEPTION code=0x{ev.ExceptionCode:X8} first={ev.FirstChance} address=0x{ev.ExceptionAddress:X}");
            if (ev.ExceptionCode == 0xc000027b && ev.ParameterCount >= 2)
            {
                var pointers = Read(ev.Parameter0, (int)Math.Min(ev.Parameter1, 8) * 8);
                for (int offset = 0; offset + 8 <= pointers.Length; offset += 8)
                {
                    var info = Read(BitConverter.ToUInt64(pointers, offset), 64);
                    if (info.Length < 32) continue;
                    Console.WriteLine($"STOWED HRESULT=0x{BitConverter.ToUInt32(info, 8):X8} form={BitConverter.ToUInt32(info, 12) & 3}");
                    if ((BitConverter.ToUInt32(info, 12) & 3) == 2)
                        Console.WriteLine(Encoding.Unicode.GetString(Read(BitConverter.ToUInt64(info, 16), 4096)).Split('\0')[0]);
                }
            }
            if (ev.ExceptionCode != 0x80000003) continuation = 0x80010001;
        }
        DebugNative.ContinueDebugEvent(ev.ProcessId, ev.ThreadId, continuation);
        if (ev.Code == 5) break;
    }
    DebugNative.DebugActiveProcessStop(process.Id);
}
finally { DebugNative.CloseHandle(process.Thread); DebugNative.CloseHandle(process.Process); }

internal static class DebugNative
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] public struct Startup
    {
        public int Size; public string? Reserved; public string? Desktop; public string? Title;
        public uint X, Y, Width, Height, CharsX, CharsY, Fill, Flags; public ushort Show, ReservedSize;
        public nint ReservedPointer, Input, Output, Error;
    }
    [StructLayout(LayoutKind.Sequential)] public struct ProcessInfo { public nint Process, Thread; public uint Id, ThreadId; }
    [StructLayout(LayoutKind.Explicit, Size = 176)] public struct Event
    {
        [FieldOffset(0)] public uint Code; [FieldOffset(4)] public uint ProcessId; [FieldOffset(8)] public uint ThreadId;
        [FieldOffset(16)] public uint ExceptionCode; [FieldOffset(32)] public ulong ExceptionAddress;
        [FieldOffset(40)] public uint ParameterCount; [FieldOffset(48)] public ulong Parameter0;
        [FieldOffset(56)] public ulong Parameter1; [FieldOffset(168)] public uint FirstChance;
        [FieldOffset(16)] public ulong Pointer; [FieldOffset(24)] public ushort Unicode; [FieldOffset(26)] public ushort StringLength;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern bool CreateProcess(string application, StringBuilder command, nint procAttr, nint threadAttr, bool inherit, uint flags, nint environment, string directory, ref Startup startup, out ProcessInfo process);
    [DllImport("kernel32.dll")] public static extern bool WaitForDebugEvent(out Event ev, uint milliseconds);
    [DllImport("kernel32.dll")] public static extern bool ContinueDebugEvent(uint process, uint thread, uint status);
    [DllImport("kernel32.dll")] public static extern bool ReadProcessMemory(nint process, nint address, byte[] buffer, nuint count, out nuint read);
    [DllImport("kernel32.dll")] public static extern bool DebugActiveProcessStop(uint process);
    [DllImport("kernel32.dll")] public static extern bool CloseHandle(nint handle);
}
