using System.Globalization;
using N56Precision.Backend;

if (args.Contains("--version")) { Console.WriteLine("N56 Precision 0.2.0-preview.1 (C# worker)"); return 0; }
if (args.SequenceEqual(new[] { "--check-driver" }))
{
    try { using var device = new IoctlDevice(@"\\.\N56PrecisionBridge"); Console.WriteLine(device.Status()); return 0; }
    catch (Exception error) { Console.Error.WriteLine(error.Message); return 1; }
}
if (args.Contains("--help"))
{
    Console.WriteLine("N56Precision.Worker [--native] [--seconds N] [--config FILE] [--status-file FILE] [--stop-file FILE] [--parent-pid PID] [--preview-stream] [--invert-y] [--button-zone-percent N] [--json]"); return 0;
}
try
{
    var flags = new HashSet<string> { "--native", "--preview-stream", "--invert-y", "--json" };
    var values = new HashSet<string> { "--seconds", "--config", "--status-file", "--stop-file", "--parent-pid", "--button-zone-percent" };
    var options = new Dictionary<string, string>();
    for (int i = 0; i < args.Length; i++)
    {
        if (flags.Contains(args[i])) options[args[i]] = "true";
        else if (values.Contains(args[i]) && i + 1 < args.Length) { string key = args[i]; options[key] = args[++i]; }
        else throw new ArgumentException("Unknown option or missing value: " + args[i]);
    }
    string? Value(string name) => options.GetValueOrDefault(name);
    double Number(string name, double fallback) => Value(name) is string value ? double.Parse(value, CultureInfo.InvariantCulture) : fallback;
    double seconds = Number("--seconds", 0); double? zone = Value("--button-zone-percent") == null ? null : Number("--button-zone-percent", 15);
    if (!double.IsFinite(seconds) || seconds < 0 || (zone.HasValue && (!double.IsFinite(zone.Value) || zone is < 0 or > 100))) throw new ArgumentException("Invalid duration or button-zone percentage");
    if (Value("--config") != null && !options.ContainsKey("--native")) throw new ArgumentException("--config requires --native");
    if (zone.HasValue && Value("--config") != null) throw new ArgumentException("Set buttonZonePercent in --config instead of a CLI override");
    using var stop = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
    return WorkerRuntime.Run(new WorkerOptions(options.ContainsKey("--native"), seconds, options.ContainsKey("--invert-y"), zone,
        options.ContainsKey("--json"), Value("--config"), Value("--stop-file"), Value("--status-file"),
        Value("--parent-pid") is string parent ? int.Parse(parent, CultureInfo.InvariantCulture) : null, options.ContainsKey("--preview-stream")), stop.Token);
}
catch (Exception error) { Console.Error.WriteLine(error.Message); return 1; }
