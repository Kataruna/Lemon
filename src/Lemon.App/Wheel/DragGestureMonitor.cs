using System.Runtime.InteropServices;

namespace Lemon.App.Wheel;

/// <summary>
/// Watches system-wide mouse and keyboard input for the "open the wheel" gestures: shaking the mouse, or
/// pressing the wheel key, while files are being dragged anywhere in Windows.
/// </summary>
/// <remarks>
/// Low-level hooks run on a dedicated thread with its own message loop. Windows drops a hook whose callback
/// is slow, so the callbacks only update a little state and raise events; listeners must hand off quickly.
/// </remarks>
internal sealed class DragGestureMonitor : IDisposable
{
    private readonly Native.HookProc _mouseProc;
    private readonly Native.HookProc _keyboardProc;
    private readonly ShakeDetector _shake = new();
    private Thread? _thread;
    private uint _threadId;
    private nint _mouseHook;
    private nint _keyboardHook;

    private bool _buttonDown;
    private bool _firedThisDrag;
    private bool _swallowingKey;
    private bool _swallowingTab;

    public DragGestureMonitor()
    {
        // Keep the delegates in fields so the GC can't collect them while Windows holds pointers to them.
        _mouseProc = OnMouse;
        _keyboardProc = OnKeyboard;
    }

    /// <summary>A gesture happened during a drag. Coordinates are physical screen pixels. Raised on the hook thread.</summary>
    public event Action<int, int>? Triggered;

    /// <summary>The primary button was released, i.e. the drag ended (dropped or not). Raised on the hook thread.</summary>
    public event Action? ButtonReleased;

    /// <summary>Esc was pressed (it also cancels the drag in the source app). Raised on the hook thread.</summary>
    public event Action? EscapePressed;

    /// <summary>
    /// Tab (Shift+Tab goes back) or the mouse wheel asked for the wheel's next (+1) or previous (-1) page.
    /// Raised on the hook thread.
    /// </summary>
    public event Action<int>? PageRequested;

    /// <summary>1, 2 or 3 asked for a specific page. Raised on the hook thread.</summary>
    public event Action<WheelPage>? PageSelected;

    /// <summary>
    /// Whether the conversion wheel is up. Tab and scrolling are only taken over while it is and a drag is going on,
    /// so they work normally everywhere else. Called on the hook thread, so it must be quick.
    /// </summary>
    public Func<bool>? IsWheelOpen { get; set; }

    public bool ShakeEnabled { get; set; } = true;

    /// <summary>Virtual-key code that opens the wheel while dragging, or 0 for none.</summary>
    public int WheelKey { get; set; }

    public void Start()
    {
        if (_thread is not null)
            return;

        using var ready = new ManualResetEventSlim();
        _thread = new Thread(() =>
        {
            _threadId = Native.GetCurrentThreadId();
            var module = Native.GetModuleHandle(null);
            _mouseHook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, Marshal.GetFunctionPointerForDelegate(_mouseProc), module, 0);
            _keyboardHook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, Marshal.GetFunctionPointerForDelegate(_keyboardProc), module, 0);
            ready.Set();

            while (Native.GetMessage(out _, 0, 0, 0) > 0)
            {
            }

            Native.UnhookWindowsHookEx(_mouseHook);
            Native.UnhookWindowsHookEx(_keyboardHook);
        })
        {
            IsBackground = true,
            Name = "Lemon drag gestures",
        };
        _thread.Start();
        ready.Wait();
    }

    public void Dispose()
    {
        if (_thread is null)
            return;
        Native.PostThreadMessage(_threadId, Native.WM_QUIT, 0, 0);
        _thread.Join(TimeSpan.FromSeconds(1));
        _thread = null;
    }

    private nint OnMouse(int code, nint wParam, nint lParam)
    {
        if (code >= 0)
        {
            var info = Marshal.PtrToStructure<Native.MSLLHOOKSTRUCT>(lParam);
            switch ((int)wParam)
            {
                case Native.WM_LBUTTONDOWN:
                    _buttonDown = true;
                    _firedThisDrag = false;
                    _shake.Reset();
                    break;

                case Native.WM_LBUTTONUP:
                    if (_buttonDown)
                    {
                        _buttonDown = false;
                        ButtonReleased?.Invoke();
                    }
                    break;

                case Native.WM_MOUSEWHEEL when IsWheelOpen?.Invoke() == true && IsLeftButtonDown():
                    // The high word of mouseData is the signed wheel delta; scrolling down goes forward.
                    PageRequested?.Invoke((short)(info.MouseData >> 16) > 0 ? -1 : 1);
                    return 1;
                case Native.WM_MOUSEMOVE when _buttonDown && !_firedThisDrag && ShakeEnabled:
                    // Only look at what's being dragged once the cheap shake check passes.
                    if (_shake.Add(info.Point.X, info.Point.Y, info.Time) && Native.IsDragDropInProgress())
                        Fire(info.Point.X, info.Point.Y);
                    break;
            }
        }
        return Native.CallNextHookEx(0, code, wParam, lParam);
    }

    private nint OnKeyboard(int code, nint wParam, nint lParam)
    {
        if (code >= 0)
        {
            var info = Marshal.PtrToStructure<Native.KBDLLHOOKSTRUCT>(lParam);
            var message = (int)wParam;
            var isDown = message is Native.WM_KEYDOWN or Native.WM_SYSKEYDOWN;
            var isUp = message is Native.WM_KEYUP or Native.WM_SYSKEYUP;

            if (isDown && info.VirtualKey == Native.VK_ESCAPE)
                EscapePressed?.Invoke();

            // 1 / 2 / 3 jump straight to Convert / Optimize / Cut out, only while the wheel is up mid-drag.
            if (isDown && info.VirtualKey is >= 0x31 and <= 0x33 && IsWheelOpen?.Invoke() == true && IsLeftButtonDown())
            {
                PageSelected?.Invoke((WheelPage)(info.VirtualKey - 0x31));
                return 1;
            }

            // When Tab is the wheel key itself, it only opens the wheel (scroll still turns rings); otherwise holding
            // it to open the wheel would turn the ring on auto-repeat and leave the wheel key's state stuck.
            if (info.VirtualKey == Native.VK_TAB && WheelKey != Native.VK_TAB)
            {
                if (isDown && (_swallowingTab || IsWheelOpen?.Invoke() == true && IsLeftButtonDown()))
                {
                    // Turn once per press; auto-repeats while it's held are swallowed too.
                    if (!_swallowingTab)
                        PageRequested?.Invoke((Native.GetAsyncKeyState(Native.VK_SHIFT) & 0x8000) != 0 ? -1 : 1);
                    _swallowingTab = true;
                    return 1;
                }
                if (isUp && _swallowingTab)
                {
                    _swallowingTab = false;
                    return 1;
                }
            }

            if (WheelKey != 0 && info.VirtualKey == WheelKey)
            {
                // Swallow the key (and its auto-repeats and release) only during a real drag, so it still works
                // normally everywhere else, e.g. Space while holding the mouse in a game.
                if (isDown && (_swallowingKey || IsLeftButtonDown() && Native.IsDragDropInProgress()))
                {
                    _swallowingKey = true;
                    if (!_firedThisDrag && Native.GetCursorPos(out var point))
                        Fire(point.X, point.Y);
                    return 1;
                }
                if (isUp && _swallowingKey)
                {
                    _swallowingKey = false;
                    return 1;
                }
            }
        }
        return Native.CallNextHookEx(0, code, wParam, lParam);
    }

    private void Fire(int x, int y)
    {
        _firedThisDrag = true;
        Triggered?.Invoke(x, y);
    }

    // The hook may start mid-drag and miss the button press, so ask Windows directly.
    private bool IsLeftButtonDown() => _buttonDown || (Native.GetAsyncKeyState(0x01) & 0x8000) != 0;
}
