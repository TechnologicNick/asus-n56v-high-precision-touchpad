using System.Text.Json;
using N56Precision;
using N56Precision.Backend;

var config = new Settings();
void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
Check(Settings.Catalog.Count == 14, "Must expose fourteen ASUS gesture settings");
Check(Settings.Catalog.Select(g => g.Index).Order().SequenceEqual(Enumerable.Range(1, 14)), "Incorrect ASUS DWORD indices");
Check(config.Windows.Keys.Order().SequenceEqual(new[] { "2", "3", "4", "5" }), "Incorrect forwarding counts");
Check(config.Windows["2"] && !config.Windows["3"] && config.Windows["4"] && !config.Windows["5"], "Personal hybrid defaults must use Windows for two/four fingers");
Check(config.RightClickHoldMs == 40 && config.RightClickTapMaxMs == 200, "Personal hold + tap defaults mismatch");
Check(config.ButtonZonePercent == 15, "Default button zone must be 15 percent");
Check(config.ButtonZoneAtLowY, "Button-zone sensor Y must be reversed by default");
config.ButtonZonePercent = 22;
config.RightClickHoldMs = 450;
config.Windows["3"] = false;
config.Asus["three_down"] = false;
var json = JsonSerializer.Serialize(config);
Check(json.Contains("\"windows\"") && json.Contains("\"asus\"") && json.Contains("\"version\""), "Worker JSON contract mismatch");
var roundtrip = JsonSerializer.Deserialize<Settings>(json)!;
Check(!roundtrip.Windows["3"] && !roundtrip.Asus["three_down"] && roundtrip.Windows["2"], "Switches failed to roundtrip");
Check(roundtrip.ButtonZonePercent == 22, "Button zone percentage failed to roundtrip");
Check(roundtrip.RightClickHoldMs == 450 && roundtrip.ChordRightClickEnabled, "Right-click preferences failed to roundtrip");
var legacy = JsonSerializer.Deserialize<Settings>("{\"version\":1}")!;
Check(legacy.ButtonZonePercent == 15, "Existing configs must default to a 15-percent zone");
Console.WriteLine("PASS: desktop gesture catalog, defaults, and worker JSON contract.");
var directory = Path.Combine(Path.GetTempPath(), "n56-sharing-" + Guid.NewGuid());
Directory.CreateDirectory(directory);
try
{
    string status = Path.Combine(directory, "status.json"), replacement = Path.Combine(directory, "replacement.tmp");
    File.WriteAllText(status, "{\"frames\":1}");
    using (var reader = new FileStream(status, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
    {
        File.WriteAllText(replacement, "{\"frames\":2}");
        // Windows can still deny replacement of an open file even with delete sharing.
        // Exercise the actual worker publisher: this must never terminate forwarding.
        JsonFiles.PublishStatus(status, new { frames = 2 });
    }
    File.WriteAllText(replacement, "{\"frames\":3}");
    File.Move(replacement, status, true);
    Check(Settings.ReadSharedText(status).Contains("3"), "Status updates did not resume after reader closed");
    Console.WriteLine("PASS: locked Windows status files are non-fatal and updates resume after reader closes.");
}
finally { Directory.Delete(directory, true); }
CoreTests.Run();
DescriptorTests.Run();
if (args is ["--hardware-smoke", var executable])
{
    byte[] before;
    using (var asus = new AsusSettings()) before = asus.Read();
    var start = new System.Diagnostics.ProcessStartInfo(Path.GetFullPath(executable)) { UseShellExecute = false, CreateNoWindow = true };
    foreach (var argument in new[] { "--native", "--seconds", "3", "--config", Settings.FilePath, "--parent-pid", Environment.ProcessId.ToString() }) start.ArgumentList.Add(argument);
    using var process = System.Diagnostics.Process.Start(start)!;
    Check(process.WaitForExit(15000), "Hardware smoke worker did not exit gracefully");
    Check(process.ExitCode == 0, "C# native hardware smoke failed");
    using var after = new AsusSettings(); var restored = after.Read();
    Check(before.AsSpan(20).SequenceEqual(restored.AsSpan(20)), "C# worker did not restore all eighteen ASUS preference DWORDs");
    Check(System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(restored.AsSpan(16)) == 0, "C# worker left raw feed enabled");
    Console.WriteLine("PASS: non-elevated C# worker, live ASUS pipe, native HID, and complete ASUS preference restoration.");
}
