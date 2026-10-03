using System.Text.Json;

namespace N56Precision.Backend;

public static class JsonFiles
{
    public static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    public static bool PublishStatus(string? path, object data)
    {
        if (path == null) return false;
        string temporary = path + "." + Environment.ProcessId + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(temporary, JsonSerializer.Serialize(data, Options)); File.Move(temporary, path, true); return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return false; }
        finally { try { File.Delete(temporary); } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { } }
    }
}
