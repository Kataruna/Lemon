using System.Runtime.InteropServices;

namespace Lemon.App.Shell;

/// <summary>
/// Lemon's icon in the notification area, its right-click menu, and the pop-up notifications
/// (shown as Windows notifications on Windows 10/11). Must be created and used on the UI thread,
/// whose message loop delivers the icon's window messages.
/// </summary>
internal sealed unsafe partial class TrayIcon : IDisposable
{
    public sealed record MenuItem(string Text, Action OnClick, bool IsChecked = false, bool IsDefault = false);

    private const string ClassName = "LemonTrayWindow";
    private const uint CallbackMessage = 0x8000 + 1; // WM_APP + 1
    private const uint IconId = 1;

    private readonly WndProc _wndProc;
    private readonly Func<IReadOnlyList<MenuItem?>> _menu;
    private readonly uint _taskbarCreated;
    private readonly nint _hwnd;
    private readonly nint _icon;
    private readonly string _tooltip;
    private bool _disposed;

    /// <param name="menu">Builds the right-click menu each time it opens; null entries are separators.</param>
    public TrayIcon(string iconPath, string tooltip, Func<IReadOnlyList<MenuItem?>> menu)
    {
        _menu = menu;
        _tooltip = tooltip;
        _wndProc = OnMessage; // kept in a field so the GC can't collect it while Windows calls it

        var instance = GetModuleHandleW(null);
        fixed (char* className = ClassName)
        {
            var windowClass = new WNDCLASSEXW
            {
                Size = (uint)sizeof(WNDCLASSEXW),
                WndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
                Instance = instance,
                ClassName = (nint)className,
            };
            if (RegisterClassExW(ref windowClass) == 0)
                ErrorLog.Write("Registering the tray window class", new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError()));
            // A hidden top-level window (not message-only) so it also hears "TaskbarCreated" after Explorer restarts.
            _hwnd = CreateWindowExW(0, className, className, 0x80000000 /* WS_POPUP */, 0, 0, 0, 0, 0, 0, instance, 0);
            if (_hwnd == 0)
                ErrorLog.Write("Creating the tray window", new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError()));
        }

        _taskbarCreated = RegisterWindowMessageW("TaskbarCreated");
        _icon = LoadImageW(0, iconPath, 1 /* IMAGE_ICON */, GetSystemMetrics(49), GetSystemMetrics(50), 0x10 /* LR_LOADFROMFILE */);
        AddIcon();
    }

    /// <summary>Left click on the icon.</summary>
    public event Action? Clicked;

    /// <summary>The user clicked the most recent notification.</summary>
    public event Action? NotificationClicked;

    /// <summary>Shows a Windows notification coming from the tray icon.</summary>
    public void ShowNotification(string title, string message, bool isError)
    {
        var data = NewData(NIF_INFO);
        Copy(title, data.InfoTitle, 64);
        Copy(message, data.Info, 256);
        data.InfoFlags = isError ? NIIF_ERROR : NIIF_INFO;
        Shell_NotifyIconW(NIM_MODIFY, &data);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        var data = NewData(0);
        Shell_NotifyIconW(NIM_DELETE, &data);
        DestroyWindow(_hwnd);
        DestroyIcon(_icon);
    }

    private void AddIcon()
    {
        var data = NewData(NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP);
        data.CallbackMessage = CallbackMessage;
        data.Icon = _icon;
        Copy(_tooltip, data.Tip, 128);
        Shell_NotifyIconW(NIM_ADD, &data);
        data.Version = 4; // NOTIFYICON_VERSION_4: richer click events, coordinates in wParam
        Shell_NotifyIconW(NIM_SETVERSION, &data);
    }

    private nint OnMessage(nint hwnd, uint message, nint wParam, nint lParam)
    {
        if (message == CallbackMessage)
        {
            switch ((uint)(lParam & 0xFFFF))
            {
                case NIN_SELECT or NIN_KEYSELECT:
                    Clicked?.Invoke();
                    break;
                case WM_CONTEXTMENU:
                    ShowMenu((short)(wParam & 0xFFFF), (short)((wParam >> 16) & 0xFFFF));
                    break;
                case NIN_BALLOONUSERCLICK:
                    NotificationClicked?.Invoke();
                    break;
            }
            return 0;
        }
        if (message == _taskbarCreated && _taskbarCreated != 0)
        {
            AddIcon();
            return 0;
        }
        return DefWindowProcW(hwnd, message, wParam, lParam);
    }

    private void ShowMenu(int x, int y)
    {
        var items = _menu();
        var menu = CreatePopupMenu();
        try
        {
            for (var i = 0; i < items.Count; i++)
            {
                if (items[i] is not { } item)
                {
                    AppendMenuW(menu, MF_SEPARATOR, 0, null);
                    continue;
                }
                AppendMenuW(menu, MF_STRING | (item.IsChecked ? MF_CHECKED : 0), (nuint)(i + 1), item.Text);
                if (item.IsDefault)
                    SetMenuDefaultItem(menu, (uint)(i + 1), 0);
            }

            // Without this the menu doesn't close when the user clicks elsewhere (documented Win32 quirk).
            SetForegroundWindow(_hwnd);
            var chosen = TrackPopupMenuEx(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON | TPM_BOTTOMALIGN, x, y, _hwnd, 0);
            PostMessageW(_hwnd, 0 /* WM_NULL */, 0, 0);
            if (chosen > 0 && items[chosen - 1] is { } picked)
                picked.OnClick();
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    private NOTIFYICONDATAW NewData(uint flags) => new()
    {
        Size = (uint)sizeof(NOTIFYICONDATAW),
        Window = _hwnd,
        Id = IconId,
        Flags = flags,
    };

    private static void Copy(string text, char* destination, int capacity)
    {
        var length = Math.Min(text.Length, capacity - 1);
        text.AsSpan(0, length).CopyTo(new Span<char>(destination, capacity));
        destination[length] = '\0';
    }

    // ---- Win32 ----

    private const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2, NIM_SETVERSION = 4;
    private const uint NIF_MESSAGE = 0x1, NIF_ICON = 0x2, NIF_TIP = 0x4, NIF_INFO = 0x10, NIF_SHOWTIP = 0x80;
    private const uint NIIF_INFO = 0x1, NIIF_ERROR = 0x3;
    private const uint NIN_SELECT = 0x400, NIN_KEYSELECT = 0x401, NIN_BALLOONUSERCLICK = 0x405, WM_CONTEXTMENU = 0x7B;
    private const uint MF_STRING = 0x0, MF_CHECKED = 0x8, MF_SEPARATOR = 0x800;
    private const uint TPM_RIGHTBUTTON = 0x2, TPM_BOTTOMALIGN = 0x20, TPM_RETURNCMD = 0x100;

    private delegate nint WndProc(nint hwnd, uint message, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct WNDCLASSEXW
    {
        public uint Size;
        public uint Style;
        public nint WndProc;
        public int ClassExtra;
        public int WindowExtra;
        public nint Instance;
        public nint Icon;
        public nint Cursor;
        public nint Background;
        public nint MenuName;
        public nint ClassName;
        public nint SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NOTIFYICONDATAW
    {
        public uint Size;
        public nint Window;
        public uint Id;
        public uint Flags;
        public uint CallbackMessage;
        public nint Icon;
        public fixed char Tip[128];
        public uint State;
        public uint StateMask;
        public fixed char Info[256];
        public uint Version;
        public fixed char InfoTitle[64];
        public uint InfoFlags;
        public Guid Item;
        public nint BalloonIcon;
    }

    [LibraryImport("shell32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool Shell_NotifyIconW(uint message, NOTIFYICONDATAW* data);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial ushort RegisterClassExW(ref WNDCLASSEXW windowClass);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint CreateWindowExW(uint exStyle, char* className, char* windowName, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    private static partial nint DefWindowProcW(nint hwnd, uint message, nint wParam, nint lParam);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint RegisterWindowMessageW(string name);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint GetModuleHandleW(string? name);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint LoadImageW(nint instance, string name, uint type, int width, int height, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(nint icon);

    [LibraryImport("user32.dll")]
    private static partial int GetSystemMetrics(int index);

    [LibraryImport("user32.dll")]
    private static partial nint CreatePopupMenu();

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AppendMenuW(nint menu, uint flags, nuint id, string? text);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetMenuDefaultItem(nint menu, uint item, uint byPosition);

    [LibraryImport("user32.dll")]
    private static partial int TrackPopupMenuEx(nint menu, uint flags, int x, int y, nint hwnd, nint parameters);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyMenu(nint menu);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostMessageW(nint hwnd, uint message, nint wParam, nint lParam);
}
