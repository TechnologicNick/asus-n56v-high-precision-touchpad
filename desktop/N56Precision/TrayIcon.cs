using System.Runtime.InteropServices;

namespace N56Precision;

internal sealed class TrayIcon : IDisposable
{
    private const uint TrayMessage = 0x8000 + 56;
    private readonly nint hwnd;
    private readonly Native.SubclassProcedure callback;
    private Native.NotifyIconData icon;
    private readonly Action show;
    private readonly Action toggle;
    private readonly Action quit;
    private readonly Func<bool> running;
    public TrayIcon(nint hwnd, Action show, Action toggle, Action quit, Func<bool> running)
    {
        this.hwnd = hwnd; this.show = show; this.toggle = toggle; this.quit = quit; this.running = running;
        callback = Procedure;
        if (!Native.SetWindowSubclass(hwnd, callback, 56, 0)) throw new InvalidOperationException("Cannot attach tray message handler.");
        icon = new Native.NotifyIconData { Size = (uint)Marshal.SizeOf<Native.NotifyIconData>(), Window = hwnd,
            Id = 56, Flags = 1 | 2 | 4, Callback = TrayMessage, Icon = Native.LoadIcon(0, 32516),
            Tip = "N56 Precision — click to configure gestures", Info = "", InfoTitle = "" };
        if (!Native.NotifyIcon(0, ref icon))
        {
            Native.RemoveWindowSubclass(hwnd, callback, 56);
            throw new InvalidOperationException("Cannot create notification-area icon.");
        }
    }
    private nint Procedure(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        if (message == Native.TaskbarCreated) Native.NotifyIcon(0, ref icon);
        if (message == Native.ShowMessage) { show(); return 0; }
        if (message == TrayMessage)
        {
            uint notification = (uint)(long)lParam & 0xffff;
            if (notification is 0x202 or 0x203 or 0x400 or 0x401) show();
            else if (notification == 0x205) Menu();
            return 0;
        }
        return Native.DefSubclassProc(window, message, wParam, lParam);
    }
    private void Menu()
    {
        nint menu = Native.CreatePopupMenu();
        if (menu == 0) return;
        try
        {
            Native.AppendMenu(menu, 0, 1, "Open gesture settings");
            Native.AppendMenu(menu, 0, 2, running() ? "Pause bridge" : "Resume bridge");
            Native.AppendMenu(menu, 0x800, 0, null);
            Native.AppendMenu(menu, 0, 3, "Exit and restore ASUS settings");
            Native.GetCursorPos(out var point);
            Native.SetForegroundWindow(hwnd);
            uint selected = Native.TrackPopupMenu(menu, 0x100 | 0x2, point.X, point.Y, 0, hwnd, 0);
            Native.PostMessage(hwnd, 0, 0, 0);
            if (selected == 1) show();
            else if (selected == 2) toggle();
            else if (selected == 3) quit();
        }
        finally { Native.DestroyMenu(menu); }
    }
    public void Dispose()
    {
        Native.NotifyIcon(2, ref icon);
        Native.RemoveWindowSubclass(hwnd, callback, 56);
    }
}
