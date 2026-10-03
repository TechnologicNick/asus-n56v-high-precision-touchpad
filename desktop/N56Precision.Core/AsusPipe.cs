using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;

namespace N56Precision.Backend;

public sealed class AsusPipe : IDisposable
{
    private SafeFileHandle? command, report;
    private bool listening;
    public Action? BeforeStopping { get; set; }
    private static SafeFileHandle Open(string suffix)
    {
        string path = @"\\.\pipe\ASUS_Smart_Gesture_Raw_Data_Request_" + suffix;
        Win32.Check(Win32.WaitNamedPipeW(path, 2000));
        var handle = Win32.CreateFileW(path, 0xC0000000, 0, 0, 3, 0, 0);
        if (handle.IsInvalid) { handle.Dispose(); throw new System.ComponentModel.Win32Exception(); }
        return handle;
    }
    public AsusPipe()
    {
        try { command = Open("Cmd"); report = Open("Report"); }
        catch { Dispose(); throw; }
    }
    public void Command(string value)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(value);
        Win32.Check(Win32.WriteFile(command!, bytes, (uint)bytes.Length, out uint written, 0));
        if (written != bytes.Length) throw new IOException("Short ASUS command write");
    }
    public byte[] ReadAvailable()
    {
        Win32.Check(Win32.PeekNamedPipe(report!, 0, 0, 0, out uint available, 0));
        if (available == 0) return Array.Empty<byte>();
        var bytes = new byte[Math.Min(available, 65536)];
        Win32.Check(Win32.ReadFile(report!, bytes, (uint)bytes.Length, out uint read, 0));
        return bytes[..(int)read];
    }
    public (int Width, int Height) LogicalSize()
    {
        Command("GetLogicalSize"); var timer = Stopwatch.StartNew(); string data = "";
        while (timer.Elapsed.TotalSeconds < 2)
        {
            data += Encoding.ASCII.GetString(ReadAvailable());
            var match = Regex.Match(data, @"\ALogicalSize=(\d+),(\d+)\z");
            if (match.Success && int.TryParse(match.Groups[1].Value, out int width) && width > 0 && int.TryParse(match.Groups[2].Value, out int height) && height > 0)
            {
                Thread.Sleep(20); byte[] extra = ReadAvailable();
                if (extra.Length > 0) { data += Encoding.ASCII.GetString(extra); continue; }
                return (width, height);
            }
            Thread.Sleep(5);
        }
        throw new TimeoutException("No valid ASUS logical-size response");
    }
    public void Start() { Command("StartListeningData"); listening = true; }
    public void Dispose()
    {
        Exception? restoreError = null;
        try { BeforeStopping?.Invoke(); } catch (Exception error) { restoreError = error; }
        BeforeStopping = null;
        if (listening && command is { IsClosed: false })
        {
            try { Command("StopListeningData"); Thread.Sleep(30); } catch (System.ComponentModel.Win32Exception) { }
        }
        listening = false; report?.Dispose(); command?.Dispose();
        if (restoreError != null) throw new IOException("Could not restore ASUS settings", restoreError);
    }
}
