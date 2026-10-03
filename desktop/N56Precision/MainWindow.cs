using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Windowing;
using System.Diagnostics;
using System.Text.Json;
using Windows.Graphics;
using Shapes = Microsoft.UI.Xaml.Shapes;

namespace N56Precision;

public sealed class MainWindow : Window
{
    private readonly Settings settings = Settings.Load();
    private readonly BridgeWorker worker = new();
    private readonly Dictionary<int, ToggleSwitch> windows = new();
    private readonly Dictionary<string, ToggleSwitch> asus = new();
    private readonly Dictionary<string, TextBlock> ownership = new();
    private readonly Dictionary<string, ScrollViewer> pages = new();
    private readonly NavigationView navigation = new() { PaneDisplayMode = NavigationViewPaneDisplayMode.Left,
        IsPaneOpen = true, OpenPaneLength = 210, IsSettingsVisible = false, IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed };
    private readonly TextBlock status = Text("Starting…", 14);
    private readonly TextBlock diagnostics = Text("Waiting for worker status…", 12);
    private readonly TextBlock padStatus = Text("Move your fingers to see their positions.", 12);
    private readonly Canvas pad = new() { Width = 360, Height = 216, Background = new SolidColorBrush(Microsoft.UI.Colors.Black) };
    private readonly Canvas timeline = new() { Width = 430, Height = 118, Background = new SolidColorBrush(Microsoft.UI.Colors.Black) };
    private readonly TextBlock tapStatus = Text("Hold one finger, then tap another to measure an attempt.", 12);
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer previewTimer;
    private int previewFrames = -1;
    private readonly InfoBar notice = new() { IsClosable = true };
    private readonly Button pause = new() { Content = "Pause bridge" };
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer timer;
    private readonly TrayIcon tray;
    private bool updating, busy, quitting;
    private readonly bool uiTest = Environment.GetCommandLineArgs().Contains("--ui-test");

    public MainWindow()
    {
        settings.Asus["two_tap"] = false;
        Title = "N56 Precision · Gesture settings";
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        int width = Math.Min(950, area.Width - 48), height = Math.Min(780, area.Height - 48);
        AppWindow.MoveAndResize(new RectInt32(area.X + (area.Width - width) / 2, area.Y + (area.Height - height) / 2, width, height));
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var bar = new Grid { Margin = new Thickness(20, 12, 20, 12), ColumnSpacing = 12 };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.Children.Add(status); status.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(pause, 1); bar.Children.Add(pause);
        pause.Click += async (_, _) => await ToggleWorker();
        root.Children.Add(bar);
        Grid.SetRow(notice, 1); root.Children.Add(notice);
        Grid.SetRow(navigation, 2); root.Children.Add(navigation);
        AddPage("Overview", Symbol.Home, BuildOverview());
        AddPage("Windows gestures", Symbol.TouchPointer, BuildWindows());
        AddPage("ASUS gestures", Symbol.AllApps, BuildAsus());
        AddPage("Buttons & right-click", Symbol.Edit, BuildButtons());
        AddPage("Diagnostics", Symbol.Help, BuildDiagnostics());
        var exit = new Button { Content = uiTest ? "Exit UI test" : "Exit and restore ASUS", Margin = new Thickness(12) };
        exit.Click += async (_, _) => await Quit(); navigation.PaneFooter = exit;
        navigation.SelectionChanged += (_, e) => { if (e.SelectedItem is NavigationViewItem item && item.Tag is string name) navigation.Content = pages[name]; };
        navigation.SelectedItem = navigation.MenuItems[0]; Content = root;
        AppWindow.Closing += (_, e) => { if (!quitting) { e.Cancel = true; AppWindow.Hide(); } };
        tray = new TrayIcon(WinRT.Interop.WindowNative.GetWindowHandle(this), Show,
            async () => await ToggleWorker(), async () => await Quit(), () => worker.Running);
        RefreshOwnership();
        if (!uiTest) TryAction(() => { settings.Save(); worker.Start(); });
        timer = DispatcherQueue.CreateTimer(); timer.Interval = TimeSpan.FromMilliseconds(200);
        timer.Tick += (_, _) =>
        {
            string detail = uiTest ? "UI test — hardware worker not started." : worker.Status(); diagnostics.Text = detail;
            status.Text = uiTest ? "UI test mode" : !worker.Running
                ? string.IsNullOrEmpty(worker.LastError) ? "Paused · ASUS preferences restored" : "Bridge stopped · see Diagnostics"
                : detail.StartsWith("Lift") ? "Lift all fingers to apply settings" : "Running · touchpad gestures active";
            pause.Content = worker.Running ? "Pause bridge" : "Resume bridge";
        }; timer.Start();
        previewTimer = DispatcherQueue.CreateTimer(); previewTimer.Interval = TimeSpan.FromMilliseconds(16);
        previewTimer.Tick += (_, _) =>
        {
            if (!AppWindow.IsVisible || navigation.SelectedItem is not NavigationViewItem selected || (string)selected.Tag != "Buttons & right-click") return;
            var snapshot = worker.Preview;
            if (snapshot is JsonElement frame && frame.TryGetProperty("frames", out var number) && number.GetInt32() != previewFrames)
            {
                previewFrames = number.GetInt32(); DrawPad(); DrawTimeline(frame);
            }
        }; previewTimer.Start();
    }
    private static TextBlock Text(string text, double size = 14) => new() { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap };
    private static StackPanel Page(string title, string description)
    {
        var panel = new StackPanel { Spacing = 18, Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 28, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        panel.Children.Add(Text(description)); return panel;
    }
    private void AddPage(string name, Symbol icon, StackPanel content)
    {
        pages[name] = new ScrollViewer { Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var item = new NavigationViewItem { Content = name, Tag = name, Icon = new SymbolIcon(icon) };
        AutomationProperties.SetName(item, "Tab: " + name); navigation.MenuItems.Add(item);
    }
    private StackPanel BuildOverview()
    {
        var panel = Page("Your touchpad. Your gestures.", "Choose who handles each gesture, then fine-tune your buttons. Changes save automatically and apply after all fingers lift.");
        panel.Children.Add(Text("Quick profiles", 20));
        panel.Children.Add(Text("Windows: native gestures for 2–5 fingers. ASUS: original ASUS actions. Hybrid: Windows for 2, 4 and 5 fingers; ASUS for 3."));
        var profiles = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (string name in new[] { "Windows", "ASUS", "Hybrid" })
        {
            var button = new Button { Content = name + " preset" }; button.Click += (_, _) => Preset(name); profiles.Children.Add(button);
        }
        panel.Children.Add(profiles);
        panel.Children.Add(Text("Windows forwarding takes priority over ASUS actions for the same finger count. The ASUS tab shows which switches are overridden."));
        panel.Children.Add(Text("A button finger plus one pointing finger does not scroll. Two separate gesture fingers can scroll while another finger holds a button in the bottom strip."));
        panel.Children.Add(Text("Closing this window keeps the app in the tray. Click the tray icon to reopen; right-click it to pause or exit.")); return panel;
    }
    private StackPanel BuildWindows()
    {
        var panel = Page("Windows gestures", "Send raw finger contacts to Windows. Windows recognizes scrolling, pinch and swipes; configure the actions in Windows Settings.");
        foreach (int count in Enumerable.Range(2, 4))
        {
            var control = new ToggleSwitch { Header = $"Forward {count} fingers to Windows", IsOn = settings.Windows[count.ToString()] };
            windows[count] = control; control.Toggled += (_, _) => Changed(); panel.Children.Add(control);
        }
        panel.Children.Add(Text("Five-finger forwarding does not guarantee a distinct five-finger action."));
        var button = new Button { Content = "Windows gesture settings" };
        button.Click += (_, _) => TryAction(() => Process.Start(new ProcessStartInfo("ms-settings:devices-touchpad") { UseShellExecute = true })); panel.Children.Add(button); return panel;
    }
    private StackPanel BuildAsus()
    {
        var panel = Page("ASUS gestures", "Your original ASUS actions. Choices are remembered even when Windows forwarding overrides them.");
        foreach (var gesture in Settings.Catalog)
        {
            var card = new StackPanel { Spacing = 6 };
            var control = new ToggleSwitch { Header = gesture.Label, IsOn = settings.Asus[gesture.Key], IsEnabled = gesture.Index != 5 };
            asus[gesture.Key] = control; control.Toggled += (_, _) => Changed(); card.Children.Add(control);
            card.Children.Add(Text(gesture.Description, 12)); ownership[gesture.Key] = Text("", 12); card.Children.Add(ownership[gesture.Key]);
            panel.Children.Add(new Border { Child = card, Padding = new Thickness(14), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Gray) });
        } return panel;
    }
    private StackPanel BuildButtons()
    {
        var panel = Page("Buttons & right-click", "Reserve a bottom strip for button fingers, and right-click without lifting your pointing finger.");
        panel.Children.Add(Text("Bottom button zone", 20));
        panel.Children.Add(Text("Resting fingers in this strip are excluded until lifted. Fingers already scrolling or pinching can cross into it without interruption. Two gesture fingers can scroll while a separate strip finger holds a button. Without a separate strip finger, a physical button press blocks gestures. 0% disables the strip."));
        AddNumber(panel, "Bottom percentage of touchpad height", settings.ButtonZonePercent, 0, 100, value => settings.ButtonZonePercent = value);
        var orientation = new ToggleSwitch { Header = "Reverse button-zone sensor Y", IsOn = settings.ButtonZoneAtLowY };
        orientation.Toggled += (_, _) => { settings.ButtonZoneAtLowY = orientation.IsOn; TryAction(settings.Save); DrawPad(); }; panel.Children.Add(orientation);
        panel.Children.Add(Text("If a finger at the physical bottom appears near the top below, reverse the sensor Y. This changes only the button-zone interpretation, not Windows scrolling."));
        padStatus.Height = 64;
        panel.Children.Add(new Border { Child = pad, HorizontalAlignment = HorizontalAlignment.Left, BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Gray) }); panel.Children.Add(padStatus);
        panel.Children.Add(Text("Hold + tap right-click", 20)); panel.Children.Add(Text("Put one finger down, wait for the minimum delay, then briefly tap another while the first stays down. The default 50 ms delay accepts staggered taps without requiring a long hold. Simultaneous two-finger right-click stays disabled."));
        var enable = new ToggleSwitch { Header = "Enable hold + tap right-click", IsOn = settings.ChordRightClickEnabled };
        enable.Toggled += (_, _) => { settings.ChordRightClickEnabled = enable.IsOn; TryAction(settings.Save); }; panel.Children.Add(enable);
        AddNumber(panel, "Minimum delay between fingers (milliseconds)", settings.RightClickHoldMs, 0, 5000, value => settings.RightClickHoldMs = value);
        AddNumber(panel, "Maximum second-finger tap duration (milliseconds)", settings.RightClickTapMaxMs, 50, 2000, value => settings.RightClickTapMaxMs = value);
        AddNumber(panel, "Tap movement tolerance (millimeters)", settings.RightClickSlopMm, 0.1, 10, value => settings.RightClickSlopMm = value);
        panel.Children.Add(Text("Live hold + tap timeline", 20));
        panel.Children.Add(Text("Top lane: first finger. Bottom lane: second finger. Blue marks the minimum hold; yellow marks the tap deadline. The last attempt stays visible after you lift. Measurements use the settings active when the attempt began."));
        AutomationProperties.SetName(timeline, "Hold and tap timeline"); AutomationProperties.SetName(tapStatus, "Hold and tap measurements");
        panel.Children.Add(timeline); panel.Children.Add(tapStatus);
        DrawTimeline(default); return panel;
    }
    private StackPanel BuildDiagnostics()
    {
        var panel = Page("Diagnostics", "Technical details stay here, rather than crowding the gesture controls.");
        diagnostics.FontFamily = new FontFamily("Consolas"); panel.Children.Add(diagnostics);
        var logs = new Button { Content = "Open logs" }; logs.Click += (_, _) => TryAction(() => Process.Start(new ProcessStartInfo("explorer.exe", Settings.DirectoryPath) { UseShellExecute = true })); panel.Children.Add(logs);
        panel.Children.Add(Text("Status files allow shared replacement. If a status file is locked, gesture forwarding continues and a later update is retried.")); return panel;
    }
    private void DrawPad()
    {
        pad.Children.Clear(); double strip = 216 * settings.ButtonZonePercent / 100;
        var zone = new Shapes.Rectangle { Width = 360, Height = strip, Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(160, 50, 90, 130)) };
        Canvas.SetTop(zone, 216 - strip); pad.Children.Add(zone);
        pad.Children.Add(new Shapes.Line { X1 = 180, X2 = 180, Y1 = 216 - strip, Y2 = 216, StrokeThickness = 1, Stroke = new SolidColorBrush(Microsoft.UI.Colors.White) });
        if ((worker.Preview ?? worker.Snapshot) is not JsonElement snapshot || !snapshot.TryGetProperty("contacts", out var contacts)) return;
        int count = 0, reserved = 0;
        foreach (var contact in contacts.EnumerateArray())
        {
            double x = contact.GetProperty("x").GetDouble(), y = contact.GetProperty("y").GetDouble(); if (settings.ButtonZoneAtLowY) y = 1 - y;
            bool button = contact.TryGetProperty("button", out var b) && b.ValueKind == JsonValueKind.String;
            var dot = new Shapes.Ellipse { Width = 16, Height = 16, Fill = new SolidColorBrush(button ? Microsoft.UI.Colors.Orange : Microsoft.UI.Colors.LightGreen) };
            Canvas.SetLeft(dot, Math.Clamp(x * 360 - 8, 0, 344)); Canvas.SetTop(dot, Math.Clamp(y * 216 - 8, 0, 200)); pad.Children.Add(dot); count++; if (button) reserved++;
        }
        bool dragging = snapshot.TryGetProperty("dragSuppressed", out var d) && d.GetBoolean();
        bool buttonHeld = snapshot.TryGetProperty("buttonHeld", out var held) && held.GetBoolean();
        padStatus.Text = $"{count} fingers · {reserved} reserved · green = gesture area, orange = button finger\n" + (dragging ? "Physical button/drag guard active — Windows gestures suppressed." : buttonHeld && reserved > 0 ? "Separate button finger held — two-finger scrolling allowed." : "Physical button/drag guard inactive.");
    }
    private void DrawTimeline(JsonElement frame)
    {
        timeline.Children.Clear();
        JsonElement chord = default, attempt = default;
        bool hasChord = frame.ValueKind == JsonValueKind.Object && frame.TryGetProperty("chord", out chord);
        bool hasAttempt = hasChord && chord.TryGetProperty("attempt", out attempt) && attempt.ValueKind == JsonValueKind.Object;
        double hold = hasAttempt ? attempt.GetProperty("holdMs").GetDouble() : hasChord ? chord.GetProperty("liveHoldMs").GetDouble() : 0;
        double tap = hasAttempt ? attempt.GetProperty("tapMs").GetDouble() : 0;
        double minimum = hasAttempt ? attempt.GetProperty("minimumHoldMs").GetDouble() : settings.RightClickHoldMs;
        double maximum = hasAttempt ? attempt.GetProperty("maximumTapMs").GetDouble() : settings.RightClickTapMaxMs;
        double end = Math.Max(500, Math.Max(hold + Math.Max(tap, maximum), minimum + maximum)) * 1.1;
        double X(double ms) => 82 + Math.Clamp(ms / end, 0, 1) * 336;
        void Label(string caption, double x, double y, Windows.UI.Color color)
        {
            var text = Text(caption, 11); text.Foreground = new SolidColorBrush(color); Canvas.SetLeft(text, x); Canvas.SetTop(text, y); timeline.Children.Add(text);
        }
        void Bar(double start, double duration, double y, Windows.UI.Color color)
        {
            var bar = new Shapes.Rectangle { Width = Math.Max(2, X(start + duration) - X(start)), Height = 14, Fill = new SolidColorBrush(color) };
            Canvas.SetLeft(bar, X(start)); Canvas.SetTop(bar, y); timeline.Children.Add(bar);
        }
        void Marker(double ms, Windows.UI.Color color)
        {
            timeline.Children.Add(new Shapes.Line { X1 = X(ms), X2 = X(ms), Y1 = 22, Y2 = 82, StrokeThickness = 1, Stroke = new SolidColorBrush(color) });
        }
        Label("First finger", 4, 34, Microsoft.UI.Colors.White); Label("Second finger", 4, 62, Microsoft.UI.Colors.White);
        bool accepted = hasAttempt && attempt.GetProperty("accepted").GetBoolean();
        bool rejected = hasAttempt && attempt.GetProperty("reason").GetString() != "Tap in progress" && !accepted;
        Bar(0, hold + tap, 34, Microsoft.UI.Colors.LightGreen);
        if (hasAttempt) Bar(hold, tap, 62, accepted ? Microsoft.UI.Colors.LightGreen : rejected ? Microsoft.UI.Colors.OrangeRed : Microsoft.UI.Colors.Orange);
        Marker(minimum, Microsoft.UI.Colors.LightBlue); if (hasAttempt) Marker(hold + maximum, Microsoft.UI.Colors.Yellow);
        Label("0 ms", 82, 94, Microsoft.UI.Colors.White); Label($"{end:N0} ms", 356, 94, Microsoft.UI.Colors.White);
        if (hasAttempt)
        {
            tapStatus.Text = $"{attempt.GetProperty("reason").GetString()}\nFirst hold: {hold:N0} / {minimum:N0} ms minimum · Second tap: {tap:N0} / {maximum:N0} ms maximum\nMovement: {attempt.GetProperty("motionMm").GetDouble():N2} / {attempt.GetProperty("slopMm").GetDouble():N2} mm maximum";
        }
        else if (hasChord) tapStatus.Text = $"First finger held {hold:N0} / {minimum:N0} ms · " + (chord.GetProperty("ready").GetBoolean() ? "Ready for a second-finger tap" : "Waiting for minimum hold");
    }
    private void Show() { AppWindow.Show(); Activate(); Native.SetForegroundWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)); }
    private void TryAction(Action action)
    {
        try { action(); } catch (Exception error) { notice.Title = "Could not complete that action"; notice.Message = error.Message; notice.Severity = InfoBarSeverity.Error; notice.IsOpen = true; }
    }
    private void Changed()
    {
        if (updating) return; foreach (var pair in windows) settings.Windows[pair.Key.ToString()] = pair.Value.IsOn;
        foreach (var pair in asus) settings.Asus[pair.Key] = pair.Value.IsOn; RefreshOwnership(); TryAction(settings.Save);
    }
    private void RefreshOwnership()
    {
        foreach (var gesture in Settings.Catalog) ownership[gesture.Key].Text = gesture.Index == 5 ? "Suppressed — use hold + tap right-click"
            : settings.Windows.GetValueOrDefault(gesture.Fingers.ToString()) ? "Overridden by Windows forwarding" : settings.Asus[gesture.Key] ? "Enabled in ASUS" : "Disabled";
    }
    private void AddNumber(StackPanel panel, string header, double initial, double minimum, double maximum, Action<double> apply)
    {
        var number = new NumberBox { Header = header, Value = initial, Minimum = minimum, Maximum = maximum, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
            SmallChange = maximum <= 10 ? 0.1 : maximum == 100 ? 1 : 25 };
        number.ValueChanged += (_, e) => { if (!double.IsFinite(e.NewValue)) return; apply(Math.Clamp(e.NewValue, minimum, maximum)); TryAction(settings.Save); DrawPad(); }; panel.Children.Add(number);
    }
    private void Preset(string name)
    {
        updating = true; foreach (var pair in windows) pair.Value.IsOn = name != "ASUS" && (name != "Hybrid" || pair.Key != 3);
        foreach (var gesture in Settings.Catalog) asus[gesture.Key].IsOn = gesture.Index != 5 && gesture.Default; updating = false; Changed();
    }
    private async Task ToggleWorker()
    {
        if (busy) return; busy = true; pause.IsEnabled = false;
        try { if (worker.Running) { if (!await worker.StopAsync()) throw new TimeoutException("Still restoring ASUS settings. Wait and try again."); } else worker.Start(); }
        catch (Exception error) { TryAction(() => throw error); } finally { busy = false; pause.IsEnabled = true; }
    }
    private async Task Quit()
    {
        if (busy) return; busy = true;
        try { if (!await worker.StopAsync()) throw new TimeoutException("Worker has not stopped. Exit cancelled so ASUS settings can be restored.");
            quitting = true; timer.Stop(); previewTimer.Stop(); tray.Dispose(); Close(); Application.Current.Exit(); }
        catch (Exception error) { Show(); TryAction(() => throw error); } finally { busy = false; }
    }
}
