using System.Runtime.InteropServices;

namespace Lemon.App.Wheel;

/// <summary>
/// Watches system-wide mouse and keyboard input for the "open the wheel" gestures: shaking the mouse, or
/// pressing the wheel key, while files are being dragged anywhere in Windows; or holding the wheel key on its own,
/// for the files selected in File Explorer.
/// </summary>
/// <remarks>
/// Low-level hooks run on a dedicated thread with its own message loop. Windows drops a hook whose callback
/// is slow, so the callbacks only update a little state and raise events; listeners must hand off quickly.
/// </remarks>
internal sealed class DragGestureMonitor : IDisposable
{
    /// <summary>Marks the keys Lemon sends itself, so the hook lets them through untouched.</summary>
    private const nuint OwnInput = 0x4C454D4F;

    private readonly Native.HookProc _mouseProc;
    private readonly Native.HookProc _keyboardProc;
    private readonly ShakeDetector _shake = new();
    private readonly KeyHold _hold = new();
    private Thread? _thread;
    private uint _threadId;
    private nint _mouseHook;
    private nint _keyboardHook;
    private nuint _holdTimer;

    private bool _buttonDown;
    private bool _firedThisDrag;
    private bool _swallowingKey;
    private bool _swallowingTab;
    private bool _swallowingEscape;
    private bool _swallowingClick;

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

    /// <summary>Esc was pressed while the wheel was up mid-drag; the drag goes on. Raised on the hook thread.</summary>
    public event Action? EscapePressed;

    /// <summary>
    /// Tab (Shift+Tab goes back) or the mouse wheel asked for the wheel's next (+1) or previous (-1) page.
    /// Raised on the hook thread.
    /// </summary>
    public event Action<int>? PageRequested;

    /// <summary>A number key asked for a ring (1-9) or the hub (0). Raised on the hook thread.</summary>
    public event Action<int>? PageSelected;

    /// <summary>The wheel key was held on its own long enough, at this physical screen point. Raised on the hook thread.</summary>
    public event Action<int, int>? KeyHeld;

    /// <summary>The held wheel key came up. Raised on the hook thread.</summary>
    public event Action? HeldKeyReleased;

    /// <summary>A click while the wheel is up for a held key: it picks what's under the pointer. Raised on the hook thread.</summary>
    public event Action? HeldClick;

    /// <summary>
    /// Whether the conversion wheel is up. Tab and scrolling are only taken over while it is and a drag is going on,
    /// so they work normally everywhere else. Called on the hook thread, so it must be quick.
    /// </summary>
    public Func<bool>? IsWheelOpen { get; set; }

    /// <summary>Whether the wheel is up for the files selected in Explorer (no drag). Called on the hook thread.</summary>
    public Func<bool>? IsSelectionWheelOpen { get; set; }

    public bool ShakeEnabled { get; set; } = true;

    /// <summary>Virtual-key code that opens the wheel, or 0 for none. Shift, Ctrl and Alt are their generic codes.</summary>
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

            // The hold's timer is a thread timer, so it fires here, on the hook thread, and its state needs no locking.
            while (Native.GetMessage(out var message, 0, 0, 0) > 0)
            {
                if (message.Message == Native.WM_TIMER && (nuint)message.WParam == _holdTimer)
                    OnHoldTimer();
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

    private bool SelectionOpen => IsSelectionWheelOpen?.Invoke() == true;

    /// <summary>The wheel is up and keys and scrolling are ours: mid-drag, or for a held key.</summary>
    private bool WheelTakesInput => IsWheelOpen?.Invoke() == true && (IsLeftButtonDown() || SelectionOpen);

    private nint OnMouse(int code, nint wParam, nint lParam)
    {
        if (code >= 0)
        {
            var message = (int)wParam;
            // A click while the wheel is up for a held key picks; the app under the wheel never sees it.
            if (message == Native.WM_LBUTTONDOWN && SelectionOpen)
            {
                _swallowingClick = true;
                HeldClick?.Invoke();
                return 1;
            }
            if (message == Native.WM_LBUTTONUP && _swallowingClick)
            {
                _swallowingClick = false;
                return 1;
            }
            // The wheel key held with a click or a scroll is a modifier (Shift+click), not a hold.
            if (message is Native.WM_LBUTTONDOWN or Native.WM_RBUTTONDOWN or Native.WM_MBUTTONDOWN or Native.WM_MOUSEWHEEL or Native.WM_MOUSEHWHEEL)
                _hold.Spoil();

            // Every mouse event in Windows passes through here, so the struct is only read when it's needed.
            switch (message)
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

                case Native.WM_MOUSEWHEEL when WheelTakesInput:
                    // The high word of mouseData is the signed wheel delta; scrolling down goes forward.
                    PageRequested?.Invoke((short)(Info(lParam).MouseData >> 16) > 0 ? -1 : 1);
                    return 1;
                case Native.WM_MOUSEMOVE when _buttonDown && !_firedThisDrag && ShakeEnabled:
                    // Only look at what's being dragged once the cheap shake check passes.
                    var info = Info(lParam);
                    if (_shake.Add(info.Point.X, info.Point.Y, info.Time) && Native.IsDragDropInProgress())
                        Fire(info.Point.X, info.Point.Y);
                    break;
            }
        }
        return Native.CallNextHookEx(0, code, wParam, lParam);
    }

    private unsafe nint OnKeyboard(int code, nint wParam, nint lParam)
    {
        if (code >= 0)
        {
            var info = *(Native.KBDLLHOOKSTRUCT*)lParam;
            if (info.ExtraInfo == OwnInput)
                return Native.CallNextHookEx(0, code, wParam, lParam);
            var message = (int)wParam;
            var isDown = message is Native.WM_KEYDOWN or Native.WM_SYSKEYDOWN;
            var isUp = message is Native.WM_KEYUP or Native.WM_SYSKEYUP;
            var isWheelKey = WheelKey != 0 && Generic(info.VirtualKey) == WheelKey;

            // Holding the wheel key on its own, without a drag: the wheel for the files selected in Explorer.
            if (isDown && !_swallowingKey && !IsLeftButtonDown() && _hold.Down(isWheelKey, Environment.TickCount64))
            {
                if (_holdTimer != 0)
                    Native.KillTimer(0, _holdTimer);
                _holdTimer = Native.SetTimer(0, 0, (uint)KeyHold.Delay.TotalMilliseconds + 20, 0);
            }
            // Raised even before the wheel is up: it may still be reading the selection, and must not open after this.
            if (isUp && isWheelKey && !_swallowingKey && _hold.Up())
            {
                HeldKeyReleased?.Invoke();
                // Alt let go with nothing in between opens Explorer's menu, also after Esc closed the wheel. Put a key
                // that does nothing in between, as launchers do, and send the release after it. Only over Explorer and
                // the desktop, where the wheel opens: elsewhere (Alt+Tab, an elevated window) Alt is left alone.
                if (Generic(info.VirtualKey) == Native.VK_MENU
                    && Native.ClassName(Native.GetForegroundWindow()) is "CabinetWClass" or "Progman" or "WorkerW")
                {
                    Send(Native.VK_MASK, 0, up: false);
                    Send(Native.VK_MASK, 0, up: true);
                    Send((byte)info.VirtualKey, (byte)info.ScanCode, up: true, extended: info.VirtualKey == Native.VK_RMENU);
                    return 1;
                }
            }

            // While the wheel is up, Esc only closes it and a drag carries on (shake or press the wheel key to bring
            // it back). Swallowed here, it never reaches the drag, which would cancel. Otherwise it cancels as usual.
            if (info.VirtualKey == Native.VK_ESCAPE)
            {
                if (isDown && (_swallowingEscape || WheelTakesInput))
                {
                    if (!_swallowingEscape)
                    {
                        _firedThisDrag = false;
                        _shake.Reset();
                        EscapePressed?.Invoke();
                    }
                    _swallowingEscape = true;
                    return 1;
                }
                if (isUp && _swallowingEscape)
                {
                    _swallowingEscape = false;
                    return 1;
                }
            }

            // 1-9 jump straight to that ring and 0 to the hub, only while the wheel is up.
            if (isDown && info.VirtualKey is >= 0x30 and <= 0x39 && WheelTakesInput)
            {
                PageSelected?.Invoke((int)info.VirtualKey - 0x30);
                return 1;
            }

            // When Tab is the wheel key itself, it only opens the wheel (scroll still turns rings); otherwise holding
            // it to open the wheel would turn the ring on auto-repeat and leave the wheel key's state stuck. With Alt
            // held for the wheel, taking Tab here also keeps Windows' Alt+Tab switcher away.
            if (info.VirtualKey == Native.VK_TAB && WheelKey != Native.VK_TAB)
            {
                if (isDown && (_swallowingTab || WheelTakesInput))
                {
                    // Turn once per press; auto-repeats while it's held are swallowed too. Shift+Tab goes back,
                    // unless Shift is the key being held for the wheel.
                    var back = WheelKey != Native.VK_SHIFT && (Native.GetAsyncKeyState(Native.VK_SHIFT) & 0x8000) != 0;
                    if (!_swallowingTab)
                        PageRequested?.Invoke(back ? -1 : 1);
                    _swallowingTab = true;
                    return 1;
                }
                if (isUp && _swallowingTab)
                {
                    _swallowingTab = false;
                    return 1;
                }
            }

            if (isWheelKey)
            {
                // Swallow the key (and its auto-repeats and release) only during a real drag, so it still works
                // normally everywhere else, e.g. Space while holding the mouse in a game.
                if (isDown && (_swallowingKey || IsLeftButtonDown() && Native.IsDragDropInProgress()))
                {
                    _swallowingKey = true;
                    _hold.Spoil();
                    if (!_firedThisDrag && Native.GetCursorPos(out var point))
                        Fire(point.X, point.Y);
                    return 1;
                }
                if (isUp && _swallowingKey)
                {
                    _swallowingKey = false;
                    _hold.Up();
                    return 1;
                }
            }
        }
        return Native.CallNextHookEx(0, code, wParam, lParam);
    }

    private void OnHoldTimer()
    {
        Native.KillTimer(0, _holdTimer);
        _holdTimer = 0;
        if (_hold.Due(Environment.TickCount64) && !IsLeftButtonDown() && Native.GetCursorPos(out var point))
            KeyHeld?.Invoke(point.X, point.Y);
    }

    /// <summary>The hook sees left and right Shift, Ctrl and Alt; settings name the generic key.</summary>
    private static int Generic(uint key) => key switch
    {
        0xA0 or 0xA1 => Native.VK_SHIFT,
        0xA2 or 0xA3 => 0x11,
        0xA4 or 0xA5 => Native.VK_MENU,
        _ => (int)key,
    };

    private static void Send(byte key, byte scanCode, bool up, bool extended = false) =>
        Native.keybd_event(key, scanCode, (up ? Native.KEYEVENTF_KEYUP : 0) | (extended ? Native.KEYEVENTF_EXTENDEDKEY : 0), OwnInput);

    private static unsafe Native.MSLLHOOKSTRUCT Info(nint lParam) => *(Native.MSLLHOOKSTRUCT*)lParam;

    private void Fire(int x, int y)
    {
        _firedThisDrag = true;
        Triggered?.Invoke(x, y);
    }

    // The hook may start mid-drag and miss the button press, so ask Windows directly.
    private bool IsLeftButtonDown() => _buttonDown || (Native.GetAsyncKeyState(0x01) & 0x8000) != 0;
}
