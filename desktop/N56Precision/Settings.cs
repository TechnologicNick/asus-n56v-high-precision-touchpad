using System.Text.Json;
using System.Text.Json.Serialization;

namespace N56Precision;

public sealed record Gesture(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("index")] int Index,
    [property: JsonPropertyName("fingers")] int Fingers,
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("default")] bool Default);

public sealed class Settings
{
    public static string DirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "N56PrecisionBridge");
    public static string FilePath => Path.Combine(DirectoryPath, "settings.json");
    public static List<Gesture> Catalog { get; } = JsonSerializer.Deserialize<List<Gesture>>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "gesture-catalog.json")))!;
    [JsonPropertyName("version")] public int Version { get; set; } = 1;
    [JsonPropertyName("buttonZonePercent")] public double ButtonZonePercent { get; set; } = 15;
    [JsonPropertyName("buttonZoneAtLowY")] public bool ButtonZoneAtLowY { get; set; } = true;
    [JsonPropertyName("chordRightClickEnabled")] public bool ChordRightClickEnabled { get; set; } = true;
    [JsonPropertyName("rightClickHoldMs")] public double RightClickHoldMs { get; set; } = 40;
    [JsonPropertyName("rightClickTapMaxMs")] public double RightClickTapMaxMs { get; set; } = 200;
    [JsonPropertyName("rightClickSlopMm")] public double RightClickSlopMm { get; set; } = 2;
    [JsonPropertyName("windows")] public Dictionary<string, bool> Windows { get; set; } = Enumerable.Range(2, 4).ToDictionary(n => n.ToString(), n => n is 2 or 4);
    [JsonPropertyName("asus")] public Dictionary<string, bool> Asus { get; set; } = Catalog.ToDictionary(g => g.Key, g => g.Default);
    public static Settings Load()
        => Load(FilePath);
    public static Settings Load(string path)
    {
        if (!File.Exists(path)) return new();
        var settings = JsonSerializer.Deserialize<Settings>(ReadSharedText(path)) ?? throw new InvalidDataException("Empty settings file.");
        if (settings.Version != 1 || settings.Windows is null || settings.Asus is null || !double.IsFinite(settings.ButtonZonePercent) || settings.ButtonZonePercent is < 0 or > 100 ||
            !double.IsFinite(settings.RightClickHoldMs) || settings.RightClickHoldMs is < 0 or > 5000 ||
            !double.IsFinite(settings.RightClickTapMaxMs) || settings.RightClickTapMaxMs is < 50 or > 2000 ||
            !double.IsFinite(settings.RightClickSlopMm) || settings.RightClickSlopMm is < 0.1 or > 10 ||
            !settings.Windows.Keys.Order().SequenceEqual(new[] { "2", "3", "4", "5" }) ||
            !settings.Asus.Keys.Order().SequenceEqual(Catalog.Select(g => g.Key).Order()))
            throw new InvalidDataException("Unsupported settings file. Your existing file has not been overwritten.");
        return settings;
    }
    public static string ReadSharedText(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
    public void Save()
    {
        Directory.CreateDirectory(DirectoryPath);
        string temporary = FilePath + "." + Environment.ProcessId + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, FilePath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
