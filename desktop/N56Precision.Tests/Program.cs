using System.Text.Json;
using N56Precision;

var config = new Settings();
void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
Check(Settings.Catalog.Count == 14, "Must expose fourteen ASUS gesture settings");
Check(Settings.Catalog.Select(g => g.Index).Order().SequenceEqual(Enumerable.Range(1, 14)), "Incorrect ASUS DWORD indices");
Check(config.Windows.Keys.Order().SequenceEqual(new[] { "2", "3", "4", "5" }), "Incorrect forwarding counts");
Check(config.Windows.Values.All(enabled => enabled), "Default must preserve working native gestures");
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
        var start = new System.Diagnostics.ProcessStartInfo("python") { UseShellExecute = false };
        foreach (var argument in new[] { "-c", "from precision_bridge.config import publish_status; import sys; publish_status(sys.argv[1], {'frames':2})", status }) start.ArgumentList.Add(argument);
        using var writer = System.Diagnostics.Process.Start(start)!;
        writer.WaitForExit();
        Check(writer.ExitCode == 0, "Locked status file terminated the publisher");
    }
    File.WriteAllText(replacement, "{\"frames\":3}");
    File.Move(replacement, status, true);
    Check(Settings.ReadSharedText(status).Contains("3"), "Status updates did not resume after reader closed");
    Console.WriteLine("PASS: locked Windows status files are non-fatal and updates resume after reader closes.");
}
finally { Directory.Delete(directory, true); }
