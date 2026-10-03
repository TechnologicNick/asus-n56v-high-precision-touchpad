using Microsoft.UI.Xaml;
using System.Runtime.InteropServices;

namespace N56Precision;

public partial class App : Application
{
    private MainWindow? window;
    private Mutex? instance;
    public App()
    {
        UnhandledException += (_, e) =>
        {
            Directory.CreateDirectory(Settings.DirectoryPath);
            File.WriteAllText(Path.Combine(Settings.DirectoryPath, "desktop-error.log"), e.Message + "\n" + e.Exception);
        };
        InitializeComponent();
    }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        bool uiTest = Environment.GetCommandLineArgs().Contains("--ui-test");
        instance = new Mutex(true, uiTest ? @"Local\N56Precision.Desktop.UiTest" : @"Local\N56Precision.Desktop", out bool first);
        if (!first)
        {
            Native.PostMessage(Native.HWND_BROADCAST, Native.ShowMessage, 0, 0);
            Exit();
            return;
        }
        try
        {
            Directory.CreateDirectory(Settings.DirectoryPath);
            File.WriteAllText(Path.Combine(Settings.DirectoryPath, "desktop-startup.log"), "Creating window\n");
            window = new MainWindow();
            File.AppendAllText(Path.Combine(Settings.DirectoryPath, "desktop-startup.log"), "Activating window\n");
            window.Activate();
            File.AppendAllText(Path.Combine(Settings.DirectoryPath, "desktop-startup.log"), "Window activated\n");
            if (Environment.GetCommandLineArgs().Contains("--tray")) window.AppWindow.Hide();
        }
        catch (Exception error)
        {
            Directory.CreateDirectory(Settings.DirectoryPath);
            File.WriteAllText(Path.Combine(Settings.DirectoryPath, "desktop-error.log"), error.ToString());
            Native.MessageBox(0, error.Message, "N56 Precision startup failed", 0x10);
            Exit();
        }
    }
}
