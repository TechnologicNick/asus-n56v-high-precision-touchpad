using System.Diagnostics;
using System.Text.Json;

namespace N56Precision;

public sealed class BridgeWorker
{
    public JsonElement? Snapshot { get; private set; }
    private readonly object previewLock = new();
    private JsonElement? preview;
    public JsonElement? Preview { get { lock (previewLock) return preview; } }
    private Process? process;
    private readonly string stopPath = Path.Combine(Settings.DirectoryPath, $"stop-{Environment.ProcessId}");
    private readonly string statusPath = Path.Combine(Settings.DirectoryPath, $"status-{Environment.ProcessId}.json");
    private readonly string projectRoot;
    public bool Running => process is { HasExited: false };
    public string LastError { get; private set; } = "";
    public string LogPath => Path.Combine(Settings.DirectoryPath, "worker.log");
    public BridgeWorker()
    {
        projectRoot = FindProject();
    }
    private static string FindProject()
    {
        var requested = Environment.GetEnvironmentVariable("N56_BRIDGE_ROOT");
        if (requested is not null && File.Exists(Path.Combine(requested, "precision_bridge", "__main__.py"))) return requested;
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "precision_bridge", "__main__.py"))) return directory.FullName;
        throw new DirectoryNotFoundException("Cannot find precision_bridge. Keep the app inside this project, or set N56_BRIDGE_ROOT.");
    }
    public void Start()
    {
        if (Running) return;
        process?.Dispose();
        Directory.CreateDirectory(Settings.DirectoryPath);
        File.Delete(stopPath);
        File.Delete(statusPath);
        LastError = "";
        var python = Path.Combine(projectRoot, ".venv", "Scripts", "python.exe");
        if (!File.Exists(python)) throw new FileNotFoundException("Project Python environment is missing.", python);
        var start = new ProcessStartInfo(python) { WorkingDirectory = projectRoot, UseShellExecute = false,
            CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "-u", "-m", "precision_bridge", "--native", "--config", Settings.FilePath,
            "--stop-file", stopPath, "--status-file", statusPath, "--parent-pid", Environment.ProcessId.ToString(), "--preview-stream" }) start.ArgumentList.Add(argument);
        process = new Process { StartInfo = start };
        File.WriteAllText(LogPath, $"Started {DateTime.Now:O}\n");
        lock (previewLock) preview = null;
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            if (e.Data.StartsWith("N56_FRAME "))
            {
                try { using var frame = JsonDocument.Parse(e.Data.AsMemory(10)); lock (previewLock) preview = frame.RootElement.Clone(); }
                catch (JsonException) { }
            }
            else AppendLog(e.Data);
        };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) { LastError = e.Data; AppendLog(e.Data); } };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
    }
    private readonly object logLock = new();
    private void AppendLog(string text)
    {
        lock (logLock)
        {
            try
            {
                if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 2_000_000) File.Move(LogPath, LogPath + ".previous", true);
                File.AppendAllText(LogPath, text + Environment.NewLine);
            }
            catch (IOException) { }
        }
    }
    public async Task<bool> StopAsync()
    {
        if (!Running) return true;
        File.WriteAllText(stopPath, "stop");
        var stopped = process!.WaitForExitAsync();
        // Do not forcibly kill: the worker must release fingers and restore ASUS settings.
        return await Task.WhenAny(stopped, Task.Delay(5000)) == stopped;
    }
    public string Status()
    {
        if (!Running) return string.IsNullOrEmpty(LastError) ? "Paused — ASUS preferences restored." : "Stopped: " + LastError;
        try
        {
            using var json = JsonDocument.Parse(Settings.ReadSharedText(statusPath));
            var root = json.RootElement;
            Snapshot = root.Clone();
            if (root.TryGetProperty("configError", out var error) && error.ValueKind == JsonValueKind.String)
                return "Settings error: " + error.GetString();
            if (root.TryGetProperty("pending", out var pending) && pending.GetBoolean()) return "Lift all fingers to apply your changes.";
            if (root.TryGetProperty("driver", out var driver)) return $"Active · {root.GetProperty("frames").GetInt32():N0} frames\n{driver.GetString()}";
            return "Restoring ASUS preferences…";
        }
        catch (IOException) { return "Starting bridge…"; }
        catch (UnauthorizedAccessException) { return "Waiting for bridge status…"; }
        catch (JsonException) { return "Reading bridge status…"; }
    }
}
