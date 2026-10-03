using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;

namespace N56Precision.Backend;

public sealed record WorkerOptions(bool Native, double Seconds, bool InvertY, double? ZonePercent,
    bool Json, string? ConfigPath, string? StopPath, string? StatusPath, int? ParentPid, bool PreviewStream);

public static class WorkerRuntime
{
    private static long ScanTime => (long)(Now * 10000);
    private static double Now => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
    public static int Run(WorkerOptions options, CancellationToken cancellation = default)
    {
        if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess) throw new PlatformNotSupportedException("The live bridge requires x64 Windows");
        IoctlDevice? native = null; AsusSettings? settings = null; AsusPipe? pipe = null; ParentProcess? parent = null; ReportEncoder? encoder = null;
        int frames = 0, active = 0, effective = 0, clicks = 0; bool zoneMuted = false;
        var counts = new Dictionary<int, int>(); var router = new Router();
        string? configError = null; object[] contactSnapshot = Array.Empty<object>();
        double started = Now;
        try
        {
            if (options.ParentPid is int pid) parent = new ParentProcess(pid);
            Settings config = options.ConfigPath == null ? new() : Settings.Load(options.ConfigPath), pending = config;
            if (options.ZonePercent is double percent) config.ButtonZonePercent = percent;
            string currentJson = JsonSerializer.Serialize(config), pendingJson = currentJson;
            var mouse = new MouseOutput();
            if (options.Native)
            {
                native = new IoctlDevice(@"\\.\N56PrecisionBridge");
                Log("Windows touchpad: " + native.Status(), options.Json);
                settings = new AsusSettings(); settings.Read(); MouseOutput.SuppressOrdinaryTwoFingerTap();
            }
            pipe = new AsusPipe(); var (width, height) = pipe.LogicalSize();
            Log($"ASUS sensor: {width} x {height}; " + (native == null ? "capture only" : "native multi-finger mode (C#)"), options.Json);
            encoder = new ReportEncoder(width, height, options.InvertY); var zone = new ButtonZone(width, height, options.InvertY);
            var chord = new ChordRightClick(width, height); var motion = new GestureMotionTracker(width, height); var decoder = new StreamDecoder();
            pipe.Start();
            if (settings != null)
            {
                double deadline = Now + 2;
                while (BinaryPrimitives.ReadUInt32LittleEndian(settings.Read().AsSpan(16)) != 1)
                { if (Now >= deadline) throw new TimeoutException("ASUS did not enable the raw feed"); Thread.Sleep(10); }
                pipe.BeforeStopping = settings.Restore; settings.Apply(config);
                native!.Ioctl(0x0022A000, encoder.Release(ScanTime));
            }
            double lastStatus = 0, lastPublish = 0; string status = "starting";
            while (!cancellation.IsCancellationRequested && (options.Seconds == 0 || Now - started < options.Seconds))
            {
                if ((options.StopPath != null && File.Exists(options.StopPath)) || parent?.Exited() == true) break;
                foreach (var contacts in decoder.Feed(pipe.ReadAvailable()))
                {
                    frames++; active = contacts.Count(c => c.Active); counts[active] = counts.GetValueOrDefault(active) + 1;
                    if (active == 0 && currentJson != pendingJson)
                    { settings?.Apply(pending); config = pending; currentJson = pendingJson; }
                    if (options.Json) Console.WriteLine(JsonSerializer.Serialize(new { time = Now, contacts }, JsonFiles.Options));
                    if (native == null) continue;
                    double frameTime = Now; bool buttonsDown = mouse.ButtonsDown();
                    if (chord.Update(contacts, frameTime, config, buttonsDown)) { mouse.RightClick(); clicks++; }
                    var gestureContacts = zone.Filter(contacts, config.ButtonZonePercent, config.ButtonZoneAtLowY);
                    contactSnapshot = contacts.Where(c => c.Active).Select(c => (object)new { slot = c.Slot, x = c.X / (double)width,
                        y = c.Y / (double)height, button = zone.Reserved.GetValueOrDefault(c.Slot) }).ToArray();
                    effective = gestureContacts.Count(c => c.Active);
                    bool desiredMute = zone.Occupied || buttonsDown || (zoneMuted && active > 0);
                    if (desiredMute != zoneMuted) { settings!.Apply(config, desiredMute); zoneMuted = desiredMute; }
                    var routed = router.Forward(gestureContacts, config, active, buttonsDown, zone.Reserved.Count);
                    if (motion.Update(routed, config.RightClickSlopMm)) zone.ProtectGesture(routed);
                    native.Ioctl(0x0022A000, encoder.Encode(routed, ScanTime));
                    if (options.PreviewStream)
                        Console.WriteLine("N56_FRAME " + JsonSerializer.Serialize(new { frames, contacts = contactSnapshot,
                            dragSuppressed = router.DragBlocked, buttonHeld = buttonsDown, chord = chord.Snapshot(frameTime, config) }, JsonFiles.Options));
                }
                double now = Now;
                if (now - lastStatus >= 1)
                {
                    if (options.ConfigPath != null)
                    {
                        try
                        {
                            pending = Settings.Load(options.ConfigPath); pendingJson = JsonSerializer.Serialize(pending); configError = null;
                            if (active == 0 && currentJson != pendingJson) { settings?.Apply(pending); config = pending; currentJson = pendingJson; }
                        }
                        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { configError = error.Message; }
                    }
                    status = native?.Status() ?? "capture only";
                    if (!options.Json) { Console.WriteLine($"Frames: {frames}; contact counts: {JsonSerializer.Serialize(counts)}"); if (native != null) Console.WriteLine("Windows touchpad: " + status); }
                    lastStatus = now;
                }
                if (now - lastPublish >= .1)
                {
                    JsonFiles.PublishStatus(options.StatusPath, new { state = "running", frames, counts, driver = status, configError,
                        gestureFingers = effective, buttonContacts = zone.Reserved, rightClicks = clicks, contacts = contactSnapshot,
                        dragSuppressed = router.DragBlocked, pending = currentJson != pendingJson, time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0 });
                    lastPublish = now;
                }
                Thread.Sleep(2);
            }
            return 0;
        }
        catch (Exception error) when (error is IOException or Win32Exception or UnauthorizedAccessException or TimeoutException or JsonException or ArgumentException)
        { Console.Error.WriteLine("Bridge stopped: " + error.Message); return 1; }
        finally
        {
            // Restore before StopListeningData; preserve the vendor companion's snapshot semantics.
            try { pipe?.Dispose(); } catch (Exception error) { Console.Error.WriteLine("ASUS restore: " + error.Message); }
            try { if (encoder != null) native?.Ioctl(0x0022A000, encoder.Release(ScanTime)); } catch (Win32Exception) { }
            native?.Dispose();
            try { settings?.Dispose(); } catch (Exception error) { Console.Error.WriteLine("ASUS restore: " + error.Message); }
            parent?.Dispose();
            JsonFiles.PublishStatus(options.StatusPath, new { state = "stopped", frames, time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0 });
        }
    }
    private static void Log(string message, bool json) { if (json) Console.Error.WriteLine(message); else Console.WriteLine(message); }
}
