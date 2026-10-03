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
    public bool Running => process is { HasExited: false };
    public bool DesiredRunning { get; private set; }
    private int retryCount;
    private DateTime retryAfter;
    public string LastError { get; private set; } = "";
    public string LogPath => Path.Combine(Settings.DirectoryPath, "worker.log");
    public void Start()
    {
        if (Running) return;
        DesiredRunning = true; retryCount = 0; StartProcess();
    }
    private void StartProcess()
    {
        process?.Dispose();
        Directory.CreateDirectory(Settings.DirectoryPath);
        File.Delete(stopPath);
        File.Delete(statusPath);
        LastError = "";
        var executable = Path.Combine(AppContext.BaseDirectory, "worker", "N56Precision.Worker.exe");
        if (!File.Exists(executable)) throw new FileNotFoundException("C# worker is missing. Reinstall the complete desktop package.", executable);
        var start = new ProcessStartInfo(executable) { WorkingDirectory = AppContext.BaseDirectory, UseShellExecute = false,
            CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "--native", "--config", Settings.FilePath,
            "--stop-file", stopPath, "--status-file", statusPath, "--parent-pid", Environment.ProcessId.ToString(), "--preview-stream" }) start.ArgumentList.Add(argument);
        process = new Process { StartInfo = start };
        AppendLog($"Started C# worker {DateTime.Now:O}");
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
        retryAfter = DateTime.UtcNow.AddSeconds(5);
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
        DesiredRunning = false;
        if (!Running) return true;
        File.WriteAllText(stopPath, "stop");
        var stopped = process!.WaitForExitAsync();
        // Do not forcibly kill: the worker must release fingers and restore ASUS settings.
        return await Task.WhenAny(stopped, Task.Delay(5000)) == stopped;
    }
    public string Status()
    {
        if (!Running && DesiredRunning && retryCount < 12 && DateTime.UtcNow >= retryAfter)
        {
            retryCount++;
            try { StartProcess(); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            { LastError = error.Message; retryAfter = DateTime.UtcNow.AddSeconds(5); }
        }
        if (!Running && DesiredRunning && retryCount < 12) return "Waiting for the ASUS companion or driver (startup retry)…\n" + LastError;
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
