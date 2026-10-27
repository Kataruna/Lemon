using System.Diagnostics;
using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Graphics;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace Lemon.App.Wheel;

/// <summary>
/// The round format picker that pops up under the cursor mid-drag. Drop files on a slice to convert them
/// with that preset straight away, or on the middle to just add them to the queue. Other rings hold the
/// Optimize strengths, Cut out, image sizes and the user's own rings; rest on a chip in the mode bar under the
/// ring, press Tab, a number key or scroll to switch rings. With more rings than fit on the bar (or when the user
/// asks for it) there's a hub, where the rings themselves are the slices.
/// </summary>
public sealed partial class WheelWindow : Window
{
    internal const double Diameter = 320;
    internal const double Center = Diameter / 2;
    // Leaves room inside the round window for a hovered slice to pop outward.
    internal const double OuterRadius = 146;
    internal const double InnerRadius = 60;
    internal const float PopDistance = 10;
    private const int None = -2;
    private const int CenterHit = -1;
    /// <summary>Hit codes for the mode chips: <c>ChipHit - chip index</c>.</summary>
    private const int ChipHit = -3;
    /// <summary>A chip's target that means the hub rather than a ring.</summary>
    private const int HubTarget = -1;
    internal const double ChipWidth = 74;
    internal const double ChipHeight = 22;
    internal const double BarPadding = 4;
    // The mode bar floats just under the ring, like the macOS one.
    internal const double BarTop = Diameter + 6;
    internal const double WindowHeight = BarTop + ChipHeight + 2 * BarPadding + 4;
    /// <summary>How long the dragged files must rest on a mode chip (or a ring on the hub) before the ring turns to it.</summary>
    private static readonly TimeSpan ChipDwell = TimeSpan.FromMilliseconds(450);

    /// <param name="Outward">Unit vector from the wheel's center through the middle of the slice.</param>
    private sealed record Slice(Path Shape, Path Glow, StackPanel Label, Vector3 Outward);

    private readonly List<Slice> _slices = [];
    private readonly DispatcherQueueTimer _hideTimer;
    private readonly DispatcherQueueTimer _pageTimer;
    private readonly Compositor _compositor;
    private Ellipse _hub = null!;
    private StackPanel _hubContent = null!;
    private TextBlock _hubText = null!;
    private FontIcon _hubIcon = null!;
    private Image _hubImage = null!;
    private TextBlock _hubEmoji = null!;
    private Ellipse _outline = null!;
    private Border _modeBar = null!;
    private Border _barTint = null!;
    private StackPanel _modeChips = null!;
    /// <summary>The blurred picture of what's behind the wheel, while it's open with blur on.</summary>
    private ImageMagick.MagickImage? _glass;
    /// <summary>Counts openings, so a blur that finishes after the wheel moved on is thrown away.</summary>
    private int _openCount;
    private double _scale = 1;
    /// <summary>What each chip leads to, left to right: a ring index, or <see cref="HubTarget"/>.</summary>
    private int[] _chips = [];
    private WheelLook _look = null!;

    private IReadOnlyList<string>? _paths;
    /// <summary>The dragged files (not what's inside folders), folders and web links, for the Commands ring.</summary>
    private List<string> _files = [], _folders = [], _links = [];
    private Task? _loading;
    private IReadOnlyList<Preset> _choices = [];
    /// <summary>The rings for the dragged files and their presets, once the files are known.</summary>
    private WheelPlan _plan = WheelPlan.Empty;
    private int _ring;
    /// <summary>On the hub, the slices are the rings and resting on one opens it.</summary>
    private bool _inHub;
    /// <summary>
    /// The wheel opens centered on the pointer, so the middle only leads back to the hub once the pointer has left it
    /// since this ring came up; otherwise a pause right after the shake would send the files to the hub on its own.
    /// </summary>
    private bool _centerArmed;
    /// <summary>What the middle says when it isn't the way back to the hub.</summary>
    private string _centerText = "";
    private const string BackGlyph = "";
    /// <summary>Where the drag was last seen, to re-check what's under it when the ring turns without a mouse move.</summary>
    private Point? _pointer;
    private int _hover = None;
    private bool _dropped;
    /// <summary>Up for the files selected in Explorer (a held key, no drag): it follows the pointer itself.</summary>
    private bool _selection;
    private readonly DispatcherQueueTimer _follow;
    /// <summary>Counts holds, so a selection that's read after the key came up is thrown away.</summary>
    private int _holdCount;
    /// <summary>The Explorer window (or desktop) the selection came from; the wheel closes when it's no longer in front.</summary>
    private nint _owner;
    /// <summary>Where the pointer was last seen over a selection, so the keys' lit slice stays until it moves.</summary>
    private Native.POINT? _cursor;

    private int _sizePx;
    /// <summary>Where the client area starts inside the window, past WinUI's frame.</summary>
    private PointInt32 _clientOffset;
    private double _openFraction;
    private EventHandler<object>? _openTick;
    private bool _closing;

    public WheelWindow()
    {
        InitializeComponent();
        SystemBackdrop = new TransparentBackdrop();

        var presenter = OverlappedPresenter.Create();
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        presenter.SetBorderAndTitleBar(false, false);
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;

        var hwnd = Handle;
        // Never take focus from the app the user is dragging from, and stay out of Alt+Tab.
        var style = Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE);
        Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE, (nint)((long)style | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW));
        // Windows 11 would round the corners of the square window, fighting the circular region.
        var noRounding = Native.DWMWCP_DONOTROUND;
        Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_WINDOW_CORNER_PREFERENCE, ref noRounding, sizeof(int));
        // Let DWM blend the see-through pixels with what's behind (otherwise they come out black).
        var sheet = new Native.MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        Native.DwmExtendFrameIntoClientArea(hwnd, ref sheet);
        _hideTimer = DispatcherQueue.CreateTimer();
        _hideTimer.IsRepeating = false;
        _hideTimer.Tick += (_, _) => Hide();
        _pageTimer = DispatcherQueue.CreateTimer();
        _pageTimer.IsRepeating = false;
        _pageTimer.Interval = ChipDwell;
        _pageTimer.Tick += (_, _) =>
        {
            if (_hover <= ChipHit && _chips.ElementAtOrDefault(ChipHit - _hover) is var target)
                GoTo(target, target == HubTarget || target > _ring ? 1 : -1);
            else if (_inHub && _hover >= 0)
                GoTo(_hover, 1);
            else if (_hover == CenterHit && CenterBack && _centerArmed)
                GoTo(HubTarget, -1);
        };

        _follow = DispatcherQueue.CreateTimer();
        _follow.Interval = TimeSpan.FromMilliseconds(16);
        _follow.Tick += (_, _) => FollowPointer();

        _compositor = ElementCompositionPreview.GetElementVisual(Root).Compositor;
        ElementCompositionPreview.GetElementVisual(Ring).CenterPoint = new Vector3((float)Center, (float)Center, 0);
        BuildHub();
    }


    public nint Handle => WinRT.Interop.WindowNative.GetWindowHandle(this);

    public bool IsOpen { get; private set; }

    /// <summary>Inside a ring with the hub on, the middle is the way back to it (unless Settings asks for a chip instead).</summary>
    private bool CenterBack => _plan.HubOn && !_inHub && !App.Settings.Wheel.BackChip;

    /// <summary>Pops the wheel up centered on a physical screen point, kept inside that monitor's work area.</summary>
    public void ShowAt(int x, int y)
    {
        _hideTimer.Stop();
        StopOpenAnimation();
        _closing = false;
        _selection = false;
        _follow.Stop();
        _paths = null;
        _files = _folders = _links = [];
        _loading = null;
        // Pick up tools installed while Lemon sat in the tray.
        App.Conversion.RefreshTools();
        _dropped = false;
        _hover = None;
        _pointer = null;
        _pageTimer.Stop();
        _plan = WheelPlan.Empty;
        _ring = 0;
        _inHub = false;
        _centerArmed = false;
        _centerText = "Drop on a format";
        ApplyLook();
        ShowSlices();
        UpdatePageButton();

        var monitor = Native.MonitorFromPoint(new Native.POINT { X = x, Y = y }, 2 /* MONITOR_DEFAULTTONEAREST */);
        var info = new Native.MONITORINFO { Size = System.Runtime.InteropServices.Marshal.SizeOf<Native.MONITORINFO>() };
        Native.GetMonitorInfo(monitor, ref info);
        var scale = Native.GetDpiForMonitor(monitor, 0, out var dpi, out _) == 0 ? dpi / 96.0 : 1.0;

        _scale = scale;
        _sizePx = (int)Math.Ceiling(Diameter * scale);
        var heightPx = (int)Math.Ceiling(WindowHeight * scale);
        var work = info.WorkArea;
        // Size the client area, not the window: WinUI keeps a thin frame around it even without a border, and
        // that frame shows up white wherever the round region reaches it.
        AppWindow.ResizeClient(new SizeInt32(_sizePx, heightPx));
        Native.GetWindowRect(Handle, out var frame);
        var origin = new Native.POINT();
        Native.ClientToScreen(Handle, ref origin);
        _clientOffset = new PointInt32(origin.X - frame.Left, origin.Y - frame.Top);
        var outer = AppWindow.Size;
        var left = Math.Clamp(x - _sizePx / 2 - _clientOffset.X, work.Left, Math.Max(work.Left, work.Right - outer.Width));
        var top = Math.Clamp(y - _sizePx / 2 - _clientOffset.Y, work.Top, Math.Max(work.Top, work.Bottom - outer.Height));

        AppWindow.Move(new PointInt32(left, top));
        // Before showing, so the snapshot is of what's behind the wheel rather than the wheel itself.
        StartGlass(left + _clientOffset.X, top + _clientOffset.Y, _sizePx, heightPx);
        SetRegion();
        SetOpenFraction(0.3);
        AppWindow.Show(activateWindow: false);
        Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        IsOpen = true;

        // Pop: the window grows out of the cursor while the ring springs open with a little twist.
        AnimateOpen(0.3, 1, TimeSpan.FromMilliseconds(240), EaseOutCubic);
        ScaleFrom(Ring, new Vector3(0.55f), Vector3.One, damping: 0.55f);
        RotateFrom(Ring, -35f, damping: 0.6f);
        RotateFrom(Mascot, -140f, damping: 0.45f, delayMs: 60);
    }

    /// <summary>The drag ended. Give a drop on us a moment to arrive (it's processed after the release), then hide.</summary>
    public void OnDragEnded()
    {
        if (IsOpen && !_dropped)
            HideAfter(TimeSpan.FromMilliseconds(400));
    }

    /// <summary>Moves to the next (or previous) ring, wrapping around; from the hub, to the first (or last) one.</summary>
    /// <param name="step">+1 for forward (Tab, scroll down), -1 for back; sets the spin direction.</param>
    public void TurnPage(int step)
    {
        var count = _plan.Rings.Count;
        if (count > 1)
            GoTo(_inHub ? (step >= 0 ? 0 : count - 1) : (_ring + step + count) % count, step);
    }

    /// <summary>A number key: 1-9 jump to that ring, 0 to the hub.</summary>
    public void GoToNumber(int number) => GoTo(number == 0 ? HubTarget : number - 1, number == 0 || number - 1 > _ring ? 1 : -1);

    /// <param name="target">A ring index, or <see cref="HubTarget"/>.</param>
    private void GoTo(int target, int step)
    {
        _pageTimer.Stop();
        if (!IsOpen || _closing || _dropped)
            return;
        if (target == HubTarget ? !_plan.HubOn || _inHub : target < 0 || target >= _plan.Rings.Count || (!_inHub && target == _ring))
            return;

        _inHub = target == HubTarget;
        if (!_inHub)
            _ring = target;
        _centerArmed = false;
        ShowSlices(animate: true);
        RotateFrom(Ring, step >= 0 ? -30f : 30f, damping: 0.6f);
        UpdatePageButton();
        // The pointer may not move after Tab or a scroll, so work out what's under it on the new ring. Only moving onto
        // something counts as resting there: otherwise a chip that turns up under a still pointer (e.g. "All rings"
        // after leaving the hub) would switch straight back.
        UpdateHover(_pointer is { } point ? HitTest(point) : None, force: true);
        _pageTimer.Stop();
    }

    /// <summary>Shrinks the wheel back into its center, then hides the window.</summary>
    public void Hide() => Close(then: null);

    private void Close(Action? then)
    {
        _hideTimer.Stop();
        _pageTimer.Stop();
        _follow.Stop();
        if (!IsOpen || _closing)
            return;
        _closing = true;

        Spring(Ring, "Scale", new Vector3(0.8f), damping: 1f, periodMs: 30);
        AnimateOpen(_openFraction, 0, TimeSpan.FromMilliseconds(170), EaseInCubic, () =>
        {
            AppWindow.Hide();
            IsOpen = false;
            _closing = false;
            // The blurred backdrop is only good for this spot on the screen; Lemon may sit in the tray for days.
            _glass?.Dispose();
            _glass = null;
            Frost.Fill = null;
            _modeBar.Background = null;
            then?.Invoke();
        });
    }

    private void HideAfter(TimeSpan delay)
    {
        _hideTimer.Interval = delay;
        _hideTimer.Start();
    }

    // ---- Drag and drop ----

    private void OnDragEnter(object sender, DragEventArgs e)
    {
        _hideTimer.Stop();
        StartLoading(e);
    }

    /// <summary>
    /// Starts reading the dragged files, once per drag. Called from DragEnter and every DragOver: when the wheel
    /// pops up under a drag already in progress, the first DragEnter sometimes arrives before the data reports
    /// its files, so a later event has to pick it up.
    /// </summary>
    private void StartLoading(DragEventArgs e)
    {
        if (_loading is null && Carries(e.DataView))
            _loading = LoadAsync(e);
    }

    /// <summary>Files, or (with a command for links) something that may be a web link.</summary>
    private static bool Carries(DataPackageView data) =>
        data.Contains(StandardDataFormats.StorageItems) || (HasLinkCommand
            && (data.Contains(StandardDataFormats.WebLink) || data.Contains(StandardDataFormats.Text)));

    private static bool HasLinkCommand => App.Settings.Wheel.Commands.Any(c => c.RunsOn == RunsOn.Links);

    /// <summary>The dragged http(s) link, from a browser's link or from dragged text that is one.</summary>
    private static async Task<string?> LinkAsync(DataPackageView data)
    {
        var uri = data.Contains(StandardDataFormats.WebLink) ? await data.GetWebLinkAsync()
            : data.Contains(StandardDataFormats.Text) && Uri.TryCreate((await data.GetTextAsync()).Trim(), UriKind.Absolute, out var text) ? text
            : null;
        return uri?.Scheme is "http" or "https" ? uri.AbsoluteUri : null;
    }

    /// <summary>The dragged items a command runs on.</summary>
    private IReadOnlyList<string> Targets(WheelCommand command) => command.RunsOn switch
    {
        RunsOn.Files => [.. _files.Where(command.Takes)],
        RunsOn.Folders => _folders,
        _ => _links,
    };

    /// <summary>Reads the dragged files before the drop so the wheel can show formats that fit them.</summary>
    private async Task LoadAsync(DragEventArgs e)
    {
        var deferral = e.GetDeferral();
        List<string> dragged = [];
        try
        {
            // A link counts as a link even when the browser adds a file for it, but only with a command for links:
            // otherwise a drag carrying both still converts its files.
            if (HasLinkCommand && await LinkAsync(e.DataView) is { } link)
                _links = [link];
            else if (e.DataView.Contains(StandardDataFormats.StorageItems))
                dragged = [.. (await e.DataView.GetStorageItemsAsync()).Select(i => i.Path).Where(p => !string.IsNullOrEmpty(p))];
        }
        catch (Exception)
        {
            // Some sources only provide virtual items (e.g. inside a zip); there's nothing on disk to convert.
        }
        finally
        {
            deferral.Complete();
        }
        await LoadAsync(dragged);
    }

    /// <summary>Looks inside the dragged (or selected) files and folders and puts up the rings for them.</summary>
    private async Task LoadAsync(List<string> dragged)
    {
        try
        {
            _folders = [.. dragged.Where(Directory.Exists)];
            _files = [.. dragged.Where(File.Exists)];
            // A dragged folder could be huge (even a whole drive), so look inside it off the UI thread. All of it: these
            // are the files a drop converts, so a cap would silently leave the rest of a big folder out.
            _paths = await Task.Run(() => QueueViewModel.ExpandFolders(dragged).ToList());
        }
        catch (Exception)
        {
            _paths = [];
        }

        try
        {
            // Only rings with something for these files, so e.g. PDFs (which can only be optimized) open on Optimize.
            // Folders on their own open on Commands: a project folder nearly always has something convertible inside.
            _plan = Plan(_paths, [.. App.Settings.Wheel.Commands.Where(c => Targets(c).Count > 0)], onCommands: _folders.Count > 0 && _folders.Count == dragged.Count);
            _ring = _plan.Start;
            _inHub = _plan.OpensOnHub;
            _centerText = !_paths.Any(FormatCatalog.IsSupported) && _plan.Rings.Count > 0 ? "Drop on a command"
                : _paths.Count == 0 ? "Lemon can't convert these"
                : _plan.Rings.Count == 0 ? "Mixed file types: add to queue"
                : "Add to queue";
            ShowSlices(animate: true);
            UpdatePageButton();
            UpdateHover(_pointer is { } point ? HitTest(point) : _hover, force: true);
        }
        catch (Exception ex)
        {
            // Never leave a broken task behind for the drop to trip over; the hub still works as "add to queue".
            ErrorLog.Write("Building the conversion wheel", ex);
            _centerText = "Add to queue";
            ShowCenter();
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (!Carries(e.DataView))
            return;
        StartLoading(e);

        _pointer = e.GetPosition(Ring);
        var hit = HitTest(_pointer.Value);
        UpdateHover(hit);
        var usable = _paths is null || _paths.Count > 0;
        // A ring on the hub, or the middle when it leads back there, isn't somewhere to drop, only to rest on.
        var drops = hit != None && !(_inHub && hit >= 0) && !(hit == CenterHit && CenterBack);
        // A slice takes anything it was offered for; the middle only files it can queue (so no links).
        e.AcceptedOperation = drops && (usable || hit >= 0) ? Operation(e.AllowedOperations) : DataPackageOperation.None;
        e.DragUIOverride.IsGlyphVisible = false;
        e.DragUIOverride.Caption = hit switch
        {
            >= 0 when _inHub && hit < _plan.Rings.Count => "Hold here for " + _plan.Rings[hit].Name,
            >= 0 when hit < _choices.Count => (_choices[hit].CommandId is not null ? "Run " : _plan.Rings[_ring].Page == WheelPage.Convert ? "Convert to " : "") + _choices[hit].Name,
            <= ChipHit when _chips.ElementAtOrDefault(ChipHit - hit) is var target && (target == HubTarget || target != _ring) =>
                "Hold here for " + (target == HubTarget ? "all rings" : _plan.Rings[target].Name),
            CenterHit when CenterBack => "Hold here for all rings",
            CenterHit when usable => "Add to Lemon",
            _ => "",
        };
    }

    private void OnDragLeave(object sender, DragEventArgs e) => UpdateHover(None);

    // ---- Held key: the files selected in File Explorer ----

    /// <summary>
    /// Opens the wheel for what's selected in the Explorer window (or on the desktop) the key was held over. Nothing
    /// selected, or the key already let go by the time the selection is read: nothing happens.
    /// </summary>
    public async void ShowForSelection(int x, int y)
    {
        if (IsOpen && !_closing)
            return;
        var hold = ++_holdCount;
        var owner = Native.GetForegroundWindow();
        var selected = await ExplorerSelection.ReadAsync(owner);
        if (hold != _holdCount || selected.Count == 0 || (IsOpen && !_closing))
            return;
        ShowAt(x, y);
        _selection = true;
        _owner = owner;
        _cursor = null;
        // Know where the pointer is before the files load: with none, loading would count the lemon as left already,
        // and a tap right away would queue the files.
        FollowPointer(force: true);
        _loading = LoadAsync([.. selected]);
        _follow.Start();
    }

    /// <summary>The held key came up. The wheel stays up; a selection still being read is thrown away.</summary>
    public void OnHeldKeyReleased() => _holdCount++;

    /// <summary>A click on the wheel enters what's under the pointer.</summary>
    public void OnHeldClick()
    {
        if (!IsSelectionOpen)
            return;
        FollowPointer(force: true);
        // The hook only knows the window's rectangle; its corners are outside the wheel, so a click there closes it too.
        if (_hover == None)
            Hide();
        else
            Enter();
    }

    /// <summary>Up for the selected files and nothing picked yet. Read from the hook thread, so only plain fields.</summary>
    public bool IsSelectionOpen => IsOpen && _selection && !_closing && !_dropped;

    /// <summary>Esc: over selected files, inside a ring it goes back to the hub. Otherwise it closes the wheel.</summary>
    public void OnEscape()
    {
        if (IsSelectionOpen && _plan.HubOn && !_inHub)
            GoTo(HubTarget, -1);
        else
            Hide();
    }

    /// <summary>A W / D S: lights the slice (or the hub's ring) before or after the lit one, from the top, wrapping round.</summary>
    public void Step(int step)
    {
        var count = _slices.Count;
        if (!IsSelectionOpen || count == 0)
            return;
        UpdateHover(_hover >= 0 ? (_hover + step + count) % count : step > 0 ? 0 : count - 1);
        // Lit from the keys, a hub ring opens on Return or a tap, not by resting there.
        _pageTimer.Stop();
    }

    /// <summary>
    /// A tap of the key, Return or a click enters what's lit: on the hub the ring, on a ring the slice runs. The lemon,
    /// once the pointer has left it, queues the files, or goes back to the hub when that's what it's for.
    /// </summary>
    public void Enter()
    {
        if (!IsSelectionOpen)
            return;
        var hit = _hover;
        if (hit <= ChipHit && _chips.ElementAtOrDefault(ChipHit - hit) is var target)
            GoTo(target, target == HubTarget || target > _ring ? 1 : -1);
        else if (_inHub && hit >= 0)
            GoTo(hit, 1);
        else if (hit == CenterHit && CenterBack)
        {
            if (_centerArmed)
                GoTo(HubTarget, -1);
        }
        else if ((hit >= 0 && hit < _choices.Count) || (hit == CenterHit && _centerArmed))
            _ = PickAsync(hit);
    }

    /// <summary>Without a drag there are no drag events, so the wheel watches the pointer itself.</summary>
    private void FollowPointer(bool force = false)
    {
        // Another window in front (Start, a click in the taskbar): close, or the keys the wheel takes would be lost there.
        if (Native.GetForegroundWindow() != _owner)
        {
            Hide();
            return;
        }
        if (!Native.GetCursorPos(out var cursor) || (!force && _cursor is { } last && last.X == cursor.X && last.Y == cursor.Y))
            return;
        _cursor = cursor;
        var origin = new Native.POINT();
        Native.ClientToScreen(Handle, ref origin);
        _pointer = new Point((cursor.X - origin.X) / _scale, (cursor.Y - origin.Y) / _scale);
        UpdateHover(HitTest(_pointer.Value));
    }

    private async Task PickAsync(int hit)
    {
        _follow.Stop();
        _dropped = true;
        _pageTimer.Stop();
        if (_loading is not null)
            await _loading;
        Act(hit);
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        var hit = HitTest(e.GetPosition(Ring));
        if (hit == None || (hit >= 0 && (_inHub || hit >= _choices.Count)) || (hit == CenterHit && CenterBack))
            return;
        // A drop on a mode chip just adds the files to the queue, like one on the hub.
        if (hit <= ChipHit)
            hit = CenterHit;

        _dropped = true;
        _pageTimer.Stop();
        // Last chance to read the files if no drag event managed to (only possible for a drop on the hub).
        StartLoading(e);
        if (_loading is not null)
            await _loading;
        Act(hit);
    }

    /// <summary>Converts (or runs a command on) the dropped or selected files with slice <paramref name="hit"/>, or queues them from the middle.</summary>
    private void Act(int hit)
    {
        // Loading may have put up a different ring, with fewer slices.
        if (hit >= _choices.Count)
            hit = CenterHit;
        if (hit >= 0 && _choices[hit].CommandId is { } id)
        {
            if (App.Settings.Wheel.Commands.FirstOrDefault(c => c.Id == id) is { } command)
            {
                // Off the UI thread: one PowerShell per item, and listing a big folder for {files}, would stall the wheel.
                var targets = Targets(command);
                _ = Task.Run(() =>
                {
                    try
                    {
                        CommandRunner.Run(command, targets);
                    }
                    catch (Exception ex)
                    {
                        ErrorLog.Write("Running a command", ex);
                    }
                });
            }
            Celebrate(hit);
            SetHub("Running " + _choices[hit].Name, icon: "");
            ScaleFrom(_hubIcon, Vector3.Zero, Vector3.One, damping: 0.4f);
            HideAfter(TimeSpan.FromMilliseconds(950));
            return;
        }
        if (_paths is not { Count: > 0 } paths)
        {
            Hide();
            return;
        }

        if (hit >= 0)
        {
            var preset = _choices[hit];
            var started = App.Queue.Add(paths, startWith: preset);
            Celebrate(hit);
            var files = started == 1 ? "1 file" : $"{started} files";
            var text = preset.IsOptimize ? $"Optimizing {files}"
                : preset.Upscale is not null ? $"Upscaling {files} {ConversionWheel.Label(preset).Title}"
                : preset.IsResize ? $"Resizing {files} to {ConversionWheel.Label(preset).Title}"
                : preset.RemoveBackground is not null ? $"Removing background from {files}"
                : $"Converting {files} to {ConversionWheel.Label(preset).Title}";
            // In a mixed drag, files the format doesn't fit wait in the queue instead.
            if (started < paths.Count(FormatCatalog.IsSupported))
                text += ", rest queued";
            SetHub(text, icon: "");
            ScaleFrom(_hubIcon, Vector3.Zero, Vector3.One, damping: 0.4f);
            HideAfter(TimeSpan.FromMilliseconds(950));
        }
        else
        {
            App.Queue.Add(paths);
            Pulse(_hub, _hubContent);
            Close(then: () =>
            {
                App.Window?.ShowConvertPage();
                App.Window?.BringToFront();
            });
        }
    }

    private static WheelPlan Plan(IReadOnlyList<string> paths, IReadOnlyList<WheelCommand> commands, bool onCommands)
    {
        var supported = paths.Where(FormatCatalog.IsSupported).ToList();
        if (supported.Count == 0)
            return commands.Count == 0 ? WheelPlan.Empty : ConversionWheel.Plan([], MediaKind.Image, App.Settings.Wheel, commands);

        // Presets depend only on the file type, so check one file per extension.
        var perType = supported
            .GroupBy(p => FormatCatalog.NormalizeExtension(p))
            .Select(g => (FormatCatalog.KindOf(g.Key)!.Value, App.Queue.PresetsFor(g.First())))
            .ToList();
        var kind = supported.GroupBy(p => FormatCatalog.KindOf(p)!.Value).MaxBy(g => g.Count())!.Key;
        return ConversionWheel.Plan(perType, kind, App.Settings.Wheel, commands, onCommands);
    }

    // ---- Hover and celebration ----

    private void UpdateHover(int hit, bool force = false)
    {
        if (hit != CenterHit)
            _centerArmed = true;
        if (hit == _hover && !force)
            return;
        var changed = hit != _hover;
        _hover = hit;

        for (var i = 0; i < _slices.Count; i++)
        {
            var slice = _slices[i];
            var on = i == hit && !_dropped;
            FadeTo(slice.Glow, on ? 1 : 0);
            var pop = on ? slice.Outward * PopDistance : Vector3.Zero;
            Spring(slice.Shape, "Translation", pop);
            Spring(slice.Glow, "Translation", pop);
            Spring(slice.Label, "Translation", on ? slice.Outward * (PopDistance + 4) : Vector3.Zero);
            Spring(slice.Label, "Scale", new Vector3(on ? 1.16f : 1f));
            foreach (var text in slice.Label.Children.OfType<FrameworkElement>())
                Recolor(text, on ? _look.OnAccent : _look.Text);
        }

        var onHub = hit == CenterHit && !_dropped;
        _hub.Fill = Brush(onHub ? "SubtleFillColorSecondaryBrush" : "CardBackgroundFillColorDefaultBrush");
        Spring(_hub, "Scale", new Vector3(onHub ? 1.1f : 1f), damping: 0.4f);
        Spring(_hubContent, "Scale", new Vector3(onHub ? 1.12f : 1f), damping: 0.4f);

        // Resting on a chip (or a ring on the hub, or the middle on the way back) turns to it, like a spring-loaded
        // folder; leaving it before then doesn't.
        StyleChips();
        var onChip = (hit <= ChipHit || (_inHub && hit >= 0) || (hit == CenterHit && CenterBack && _centerArmed)) && !_dropped;
        if (onChip && changed)
        {
            _pageTimer.Stop();
            _pageTimer.Start();
        }
        else if (!onChip)
            _pageTimer.Stop();

        // The lemon perks up whenever the pointer lands somewhere new.
        if (changed && hit != None && !_dropped)
            Wiggle(Mascot);
    }

    /// <summary>The chosen slice does a happy bounce while the others step back.</summary>
    private void Celebrate(int chosen)
    {
        for (var i = 0; i < _slices.Count; i++)
        {
            var slice = _slices[i];
            if (i == chosen)
            {
                Pulse(slice.Shape, slice.Glow, slice.Label);
                continue;
            }
            FadeTo(slice.Shape, 0.35f);
            FadeTo(slice.Glow, 0.35f);
            FadeTo(slice.Label, 0.35f);
        }
    }

    // ---- Animation helpers ----

    private void Spring(UIElement element, string property, Vector3 to, float damping = 0.5f, int periodMs = 45, int delayMs = 0)
    {
        var spring = _compositor.CreateSpringVector3Animation();
        spring.FinalValue = to;
        spring.DampingRatio = damping;
        spring.Period = TimeSpan.FromMilliseconds(periodMs);
        if (delayMs > 0)
        {
            spring.DelayTime = TimeSpan.FromMilliseconds(delayMs);
            spring.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
        }
        ElementCompositionPreview.GetElementVisual(element).StartAnimation(property, spring);
    }

    private void ScaleFrom(UIElement element, Vector3 from, Vector3 to, float damping = 0.5f, int delayMs = 0)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        visual.StopAnimation("Scale");
        visual.Scale = from;
        Spring(element, "Scale", to, damping, delayMs: delayMs);
    }

    private void RotateFrom(UIElement element, float fromDegrees, float damping = 0.5f, int delayMs = 0)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        visual.StopAnimation("RotationAngleInDegrees");
        visual.RotationAngleInDegrees = fromDegrees;
        var spring = _compositor.CreateSpringScalarAnimation();
        spring.FinalValue = 0;
        spring.DampingRatio = damping;
        spring.Period = TimeSpan.FromMilliseconds(45);
        if (delayMs > 0)
        {
            spring.DelayTime = TimeSpan.FromMilliseconds(delayMs);
            spring.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
        }
        visual.StartAnimation("RotationAngleInDegrees", spring);
    }

    /// <summary>
    /// Fades through Composition. XAML's OpacityTransition can't be used here: WinUI refuses Composition
    /// translation (the pop-out) on any element that has one.
    /// </summary>
    private void FadeTo(UIElement element, float opacity)
    {
        var fade = _compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(1f, opacity);
        fade.Duration = TimeSpan.FromMilliseconds(160);
        ElementCompositionPreview.GetElementVisual(element).StartAnimation("Opacity", fade);
    }

    private void Wiggle(UIElement element)
    {
        var wiggle = _compositor.CreateScalarKeyFrameAnimation();
        wiggle.InsertKeyFrame(0.2f, -18);
        wiggle.InsertKeyFrame(0.45f, 13);
        wiggle.InsertKeyFrame(0.7f, -6);
        wiggle.InsertKeyFrame(1f, 0);
        wiggle.Duration = TimeSpan.FromMilliseconds(450);
        ElementCompositionPreview.GetElementVisual(element).StartAnimation("RotationAngleInDegrees", wiggle);
    }

    private void Pulse(params UIElement[] elements)
    {
        var pulse = _compositor.CreateVector3KeyFrameAnimation();
        pulse.InsertKeyFrame(0.35f, new Vector3(1.12f));
        pulse.InsertKeyFrame(1f, Vector3.One);
        pulse.Duration = TimeSpan.FromMilliseconds(380);
        foreach (var element in elements)
            ElementCompositionPreview.GetElementVisual(element).StartAnimation("Scale", pulse);
    }

    /// <summary>Animates how far open the wheel is (0 = a dot at the cursor, 1 = full size).</summary>
    private void AnimateOpen(double from, double to, TimeSpan duration, Func<double, double> ease, Action? done = null)
    {
        StopOpenAnimation();
        var start = Stopwatch.GetTimestamp();
        EventHandler<object>? tick = null;
        tick = (_, _) =>
        {
            var t = Math.Min(1, Stopwatch.GetElapsedTime(start) / duration);
            SetOpenFraction(from + (to - from) * ease(t));
            if (t < 1)
                return;
            StopOpenAnimation();
            done?.Invoke();
        };
        _openTick = tick;
        CompositionTarget.Rendering += tick;
    }

    private void StopOpenAnimation()
    {
        if (_openTick is null)
            return;
        CompositionTarget.Rendering -= _openTick;
        _openTick = null;
    }

    /// <summary>Scales the whole wheel, so it grows out of / shrinks into the cursor with smooth edges.</summary>
    private void SetOpenFraction(double fraction)
    {
        _openFraction = fraction;
        var root = ElementCompositionPreview.GetElementVisual(Root);
        root.CenterPoint = new Vector3((float)Center, (float)Center, 0);
        root.Scale = new Vector3((float)Math.Max(0.01, fraction), (float)Math.Max(0.01, fraction), 1);
    }

    /// <summary>
    /// Cuts the window down to the disc and the mode bar, so the transparent corners don't catch drags meant for
    /// the windows behind. The region's edge is jagged, so it sits a little outside the drawn (anti-aliased) one.
    /// </summary>
    private void SetRegion()
    {
        var scale = _sizePx / Diameter;
        var center = _sizePx / 2.0;
        var region = Native.CreateEllipticRgn(0, 0, _sizePx + 1, _sizePx + 1);
        if (_chips.Length > 0)
        {
            var half = BarWidth / 2 * scale + 2;
            var height = (ChipHeight + 2 * BarPadding) * scale + 4;
            var top = BarTop * scale - 2;
            var bar = Native.CreateRoundRectRgn(
                (int)Math.Floor(center - half), (int)Math.Floor(top),
                (int)Math.Ceiling(center + half) + 1, (int)Math.Ceiling(top + height) + 1,
                (int)height, (int)height);
            Native.CombineRgn(region, region, bar, Native.RGN_OR);
            Native.DeleteObject(bar);
        }
        // Regions are in window coordinates; the shapes above are in client ones.
        Native.OffsetRgn(region, _clientOffset.X, _clientOffset.Y);
        // The window owns the region from here on.
        Native.SetWindowRgn(Handle, region, true);
    }

    private static double EaseOutCubic(double t) => 1 - Math.Pow(1 - t, 3);

    private static double EaseInCubic(double t) => t * t * t;

    // ---- Geometry ----

    /// <summary>Which slice a point (in Ring coordinates) is over: an index, <see cref="CenterHit"/>, or <see cref="None"/>.</summary>
    private int HitTest(Point point)
    {
        var dx = point.X - Center;
        var dy = point.Y - Center;
        var distance = Math.Sqrt(dx * dx + dy * dy);
        if (_chips.Length > 0 && Math.Abs(dx) <= BarWidth / 2
            && point.Y >= BarTop && point.Y <= BarTop + ChipHeight + 2 * BarPadding)
            return ChipHit - Math.Clamp((int)((point.X - (Center - BarWidth / 2) - BarPadding) / ChipWidth), 0, _chips.Length - 1);
        if (distance <= InnerRadius)
            return CenterHit;
        // A popped-out slice reaches a bit further, so allow for it.
        if (distance > OuterRadius + PopDistance || _slices.Count == 0)
            return None;

        // Slice 0 is centered at the top; angles grow clockwise.
        var sweep = 360.0 / _slices.Count;
        var angle = Math.Atan2(dy, dx) * 180 / Math.PI + 90 + sweep / 2;
        angle = ((angle % 360) + 360) % 360;
        return Math.Min((int)(angle / sweep), _slices.Count - 1);
    }

    private void BuildHub()
    {
        _outline = new Ellipse
        {
            Width = OuterRadius * 2 + 6,
            Height = OuterRadius * 2 + 6,
            StrokeThickness = 3,
            Margin = new Thickness(Center - OuterRadius - 3, Center - OuterRadius - 3, 0, 0),
        };
        Ring.Children.Add(_outline);

        _hub = new Ellipse
        {
            Width = InnerRadius * 2,
            Height = InnerRadius * 2,
            Fill = Brush("CardBackgroundFillColorDefaultBrush"),
            Stroke = Brush("CardStrokeColorDefaultBrush"),
            StrokeThickness = 1,
        };
        Canvas.SetLeft(_hub, Center - InnerRadius);
        Canvas.SetTop(_hub, Center - InnerRadius);
        Canvas.SetZIndex(_hub, 1);
        Ring.Children.Add(_hub);
        ElementCompositionPreview.GetElementVisual(_hub).CenterPoint = new Vector3((float)InnerRadius, (float)InnerRadius, 0);

        _hubImage = new Image { Width = 32, Height = 32, Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri("ms-appx:///Assets/Lemon.png")) };
        ElementCompositionPreview.GetElementVisual(_hubImage).CenterPoint = new Vector3(16, 16, 0);
        _hubIcon = new FontIcon { FontSize = 26, Width = 32, Height = 32, Foreground = Brush("SystemFillColorSuccessBrush"), Visibility = Visibility.Collapsed };
        ElementCompositionPreview.GetElementVisual(_hubIcon).CenterPoint = new Vector3(16, 16, 0);
        _hubText = new TextBlock
        {
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            MaxWidth = InnerRadius * 2 - 20,
            Foreground = Brush("TextFillColorSecondaryBrush"),
        };
        _hubEmoji = new TextBlock { FontSize = 28, Height = 36, FontFamily = new FontFamily("Segoe UI Emoji"), HorizontalAlignment = HorizontalAlignment.Center };
        _hubEmoji.SizeChanged += (_, _) => ElementCompositionPreview.GetElementVisual(_hubEmoji).CenterPoint =
            new Vector3((float)_hubEmoji.ActualWidth / 2, (float)_hubEmoji.ActualHeight / 2, 0);
        _hubContent = new StackPanel { Width = InnerRadius * 2, Spacing = 4, Children = { _hubImage, _hubEmoji, _hubIcon, _hubText } };
        _hubContent.SizeChanged += (_, _) => PlaceHubContent();
        Canvas.SetLeft(_hubContent, Center - InnerRadius);
        Canvas.SetZIndex(_hubContent, 2);
        Ring.Children.Add(_hubContent);

        // One chip per ring, numbered like the number keys. Outside Ring, so it doesn't spin with it.
        _modeChips = new StackPanel { Orientation = Orientation.Horizontal };
        // Glass like the disc's: the blurred picture, the tint over it, then the (translucent) card color.
        _barTint = new Border
        {
            CornerRadius = new CornerRadius(ChipHeight / 2 + BarPadding),
            Child = new Border
            {
                Padding = new Thickness(BarPadding),
                CornerRadius = new CornerRadius(ChipHeight / 2 + BarPadding),
                Background = Brush("CardBackgroundFillColorDefaultBrush"),
                Child = _modeChips,
            },
        };
        _modeBar = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, BarTop, 0, 0),
            CornerRadius = new CornerRadius(ChipHeight / 2 + BarPadding),
            // Hit-testing is done by hand in HitTest; the drag events belong to the whole window.
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
            Child = _barTint,
        };
        Root.Children.Add(_modeBar);
    }

    private void PlaceHubContent()
    {
        Canvas.SetTop(_hubContent, Center - _hubContent.ActualHeight / 2);
        ElementCompositionPreview.GetElementVisual(_hubContent).CenterPoint =
            new Vector3((float)_hubContent.ActualWidth / 2, (float)_hubContent.ActualHeight / 2, 0);
    }

    private double BarWidth => _chips.Length * ChipWidth + 2 * BarPadding;

    /// <summary>
    /// Shows a chip for each ring, or with the hub on a single one back to it if the middle isn't the way back (none
    /// on the hub itself), or no bar when there's only one ring.
    /// </summary>
    private void UpdatePageButton()
    {
        ShowCenter();
        int[] chips = _plan.Rings.Count < 2 || _inHub || CenterBack ? [] : _plan.HubOn ? [HubTarget] : [.. Enumerable.Range(0, _plan.Rings.Count)];
        if (!chips.SequenceEqual(_chips))
        {
            _chips = chips;
            _modeChips.Children.Clear();
            foreach (var target in chips)
            {
                var chip = new Border
                {
                    Width = ChipWidth,
                    Height = ChipHeight,
                    CornerRadius = new CornerRadius(ChipHeight / 2),
                    Child = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 5,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        Children =
                        {
                            new Border
                            {
                                Width = 14,
                                Height = 14,
                                CornerRadius = new CornerRadius(4),
                                BorderThickness = new Thickness(1),
                                Child = new TextBlock
                                {
                                    Text = target == HubTarget ? "0" : (target + 1).ToString(),
                                    FontSize = 9,
                                    FontWeight = FontWeights.Bold,
                                    HorizontalAlignment = HorizontalAlignment.Center,
                                    VerticalAlignment = VerticalAlignment.Center,
                                },
                            },
                            new TextBlock
                            {
                                Text = target == HubTarget ? "All rings" : _plan.Rings[target].Name,
                                FontSize = 11,
                                MaxWidth = ChipWidth - 26,
                                TextTrimming = TextTrimming.CharacterEllipsis,
                                VerticalAlignment = VerticalAlignment.Center,
                            },
                        },
                    },
                };
                ElementCompositionPreview.GetElementVisual(chip).CenterPoint = new Vector3((float)ChipWidth / 2, (float)ChipHeight / 2, 0);
                _modeChips.Children.Add(chip);
            }
            _modeBar.Visibility = chips.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            UpdateBarGlass();
            if (IsOpen)
                SetRegion();
        }
        StyleChips();
    }

    /// <summary>The current ring's chip is filled in; the one under the drag lights up.</summary>
    private void StyleChips()
    {
        for (var i = 0; i < _chips.Length; i++)
        {
            var chip = (Border)_modeChips.Children[i];
            var current = !_inHub && _chips[i] == _ring;
            var hot = _hover == ChipHit - i && !_dropped;
            chip.Background = current ? _look.Highlight
                : hot ? Brush("SubtleFillColorSecondaryBrush")
                : null;
            var foreground = current ? _look.OnAccent : _look.Text;
            var content = (StackPanel)chip.Child;
            var key = (Border)content.Children[0];
            key.BorderBrush = current ? null : Brush("TextFillColorTertiaryBrush");
            ((TextBlock)key.Child).Foreground = foreground;
            var title = (TextBlock)content.Children[1];
            title.Foreground = foreground;
            title.FontWeight = current ? FontWeights.SemiBold : FontWeights.Normal;
            Spring(chip, "Scale", new Vector3(hot ? 1.08f : 1f), damping: 0.4f);
        }
    }

    /// <summary>Lemon's lemon picture, or the emoji chosen in Settings in its place.</summary>
    private FrameworkElement Mascot => _look.UsesLemonPicture ? _hubImage : _hubEmoji;

    /// <summary>Picks up the look from Settings › Wheel (and the current light / dark theme) each time the wheel opens.</summary>
    private void ApplyLook()
    {
        _look = WheelLook.Current;
        Disc.Fill = _look.Disc;
        _barTint.Background = _look.Disc;
        Frost.Opacity = _look.GlassOpacity;
        _outline.Stroke = _look.AccentBrush;
        _hubEmoji.Text = _look.Center;
        _hubEmoji.Visibility = _look.UsesLemonPicture ? Visibility.Collapsed : Visibility.Visible;
        _hubImage.Visibility = _look.UsesLemonPicture ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Snapshots the screen where the wheel is about to appear and blurs it in the background.</summary>
    private void StartGlass(int x, int y, int width, int height)
    {
        var id = ++_openCount;
        _glass?.Dispose();
        _glass = null;
        Frost.Fill = null;
        _modeBar.Background = null;
        if (_look.Blur == 0 || Glass.Capture(x, y, width, height) is not { } picture)
            return;

        var (blur, scale) = (_look.Blur, _scale);
        _ = Task.Run(() => Glass.Blur(picture, blur, scale)).ContinueWith(task =>
        {
            if (task.Exception is { } error)
            {
                ErrorLog.Write("Blurring the wheel's glass", error);
                picture.Dispose();
            }
            else
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (id != _openCount || !IsOpen)
                    {
                        task.Result.Dispose();
                        return;
                    }
                    _glass = task.Result;
                    // The disc is 4 DIPs smaller than the window and centered in its top square.
                    var inset = (int)Math.Round(2 * _scale);
                    Frost.Fill = Glass.Crop(_glass, inset, inset, _sizePx - 2 * inset, _sizePx - 2 * inset);
                    UpdateBarGlass();
                });
        }, TaskScheduler.Default);
    }

    /// <summary>The bar's share of the blurred picture, once both the picture and the bar's width are known.</summary>
    private void UpdateBarGlass()
    {
        if (_glass is null || _chips.Length == 0)
        {
            _modeBar.Background = null;
            return;
        }
        _modeBar.Background = Glass.Crop(_glass,
            (int)Math.Round((Center - BarWidth / 2) * _scale), (int)Math.Round(BarTop * _scale),
            (int)Math.Round(BarWidth * _scale), (int)Math.Round((ChipHeight + 2 * BarPadding) * _scale));
    }

    private static void Recolor(FrameworkElement element, Brush brush)
    {
        if (element is TextBlock text)
            text.Foreground = brush;
        else if (element is FontIcon icon)
            icon.Foreground = brush;
    }

    private void SetHub(string text, string? icon)
    {
        _hubText.Text = text;
        _hubIcon.Glyph = icon ?? "";
        _hubIcon.Foreground = icon == BackGlyph ? _look.Text : Brush("SystemFillColorSuccessBrush");
        _hubIcon.Visibility = icon is null ? Visibility.Collapsed : Visibility.Visible;
        _hubImage.Visibility = icon is null && _look.UsesLemonPicture ? Visibility.Visible : Visibility.Collapsed;
        _hubEmoji.Visibility = icon is null && !_look.UsesLemonPicture ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>The middle: the way back to the hub when it leads there, otherwise what a drop on it does.</summary>
    private void ShowCenter()
    {
        if (CenterBack)
            SetHub("All rings", BackGlyph);
        else
            SetHub(_centerText, icon: null);
    }

    /// <summary>Lays out the slices: the current ring's presets, or on the hub the rings themselves.</summary>
    private void ShowSlices(bool animate = false)
    {
        _choices = _inHub || _plan.Rings.Count == 0 ? [] : _plan.Choices[_ring];
        var labels = _inHub
            ? _plan.Rings.Select(r => (r.Name, (string?)r.Detail, (string?)r.Glyph)).ToList()
            : _choices.Select(p => ConversionWheel.Label(p) is var (title, detail) ? (title, detail, CommandGlyph(p)) : default).ToList();

        foreach (var slice in _slices)
        {
            Ring.Children.Remove(slice.Shape);
            Ring.Children.Remove(slice.Glow);
            Ring.Children.Remove(slice.Label);
        }
        _slices.Clear();

        var sweep = 360.0 / Math.Max(1, labels.Count);
        var wheelCenter = new Vector3((float)Center, (float)Center, 0);
        for (var i = 0; i < labels.Count; i++)
        {
            var middle = i * sweep - 90;
            var radians = middle * Math.PI / 180;
            var outward = new Vector3((float)Math.Cos(radians), (float)Math.Sin(radians), 0);
            var geometry = Sector(middle - sweep / 2, middle + sweep / 2);

            var shape = new Path
            {
                Data = geometry,
                Fill = Brush("SubtleFillColorTransparentBrush"),
                Stroke = Brush("DividerStrokeColorDefaultBrush"),
                StrokeThickness = 1,
            };
            // A second copy in the accent color fades in on hover, which reads smoother than swapping the fill.
            var glow = new Path
            {
                Data = Sector(middle - sweep / 2, middle + sweep / 2),
                Fill = _look.Highlight,
                IsHitTestVisible = false,
            };

            var (title, detail, glyph) = labels[i];
            var label = new StackPanel { Width = 84, IsHitTestVisible = false };
            if (glyph is not null)
                label.Children.Add(new FontIcon { Glyph = glyph, FontSize = 16, Foreground = _look.Text, Margin = new Thickness(0, 0, 0, 2) });
            label.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, FontSize = 15, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = _look.Text });
            if (detail is not null)
                label.Children.Add(new TextBlock { Text = detail, FontSize = 11, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis, Opacity = 0.75, Foreground = _look.Text });

            var labelRadius = (InnerRadius + OuterRadius) / 2;
            Canvas.SetLeft(label, Center + labelRadius * Math.Cos(radians) - label.Width / 2);
            Canvas.SetTop(label, Center + labelRadius * Math.Sin(radians) - 11);
            label.SizeChanged += (_, _) =>
            {
                // Center vertically once the detail line(s) are measured, and scale around the label's middle.
                Canvas.SetTop(label, Center + labelRadius * Math.Sin(radians) - label.ActualHeight / 2);
                ElementCompositionPreview.GetElementVisual(label).CenterPoint =
                    new Vector3((float)label.ActualWidth / 2, (float)label.ActualHeight / 2, 0);
            };

            foreach (var element in new UIElement[] { shape, glow, label })
                ElementCompositionPreview.SetIsTranslationEnabled(element, true);
            ElementCompositionPreview.GetElementVisual(shape).CenterPoint = wheelCenter;
            ElementCompositionPreview.GetElementVisual(glow).CenterPoint = wheelCenter;
            ElementCompositionPreview.GetElementVisual(glow).Opacity = 0;
            ElementCompositionPreview.GetElementVisual(label).CenterPoint = new Vector3(42, 18, 0);

            Ring.Children.Insert(1 + 2 * i, shape);
            Ring.Children.Insert(2 + 2 * i, glow);
            Ring.Children.Add(label);
            _slices.Add(new Slice(shape, glow, label, outward));

            if (animate)
            {
                // Slices bloom out of the hub one after another, clockwise from the top.
                var delay = 20 + i * 30;
                ScaleFrom(shape, new Vector3(0.35f), Vector3.One, damping: 0.55f, delayMs: delay);
                ScaleFrom(glow, new Vector3(0.35f), Vector3.One, damping: 0.55f, delayMs: delay);
                ScaleFrom(label, Vector3.Zero, Vector3.One, damping: 0.5f, delayMs: delay + 40);
            }
        }
    }

    /// <summary>A command's icon, for its slice on the Commands ring.</summary>
    internal static string? CommandGlyph(Preset preset) =>
        preset.CommandId is { } id ? App.Settings.Wheel.Commands.FirstOrDefault(c => c.Id == id)?.Glyph : null;

    /// <summary>
    /// Copy when the source allows it (Explorer, most apps); a browser's link may only offer Link. Never Move, which
    /// would tell the source to delete the original.
    /// </summary>
    private static DataPackageOperation Operation(DataPackageOperation allowed) =>
        allowed.HasFlag(DataPackageOperation.Copy) || allowed == DataPackageOperation.None ? DataPackageOperation.Copy
        : allowed.HasFlag(DataPackageOperation.Link) ? DataPackageOperation.Link
        : DataPackageOperation.None;

    /// <summary>A ring slice between two angles (degrees, 0 = right, clockwise), drawn as fine polylines.</summary>
    internal static PathGeometry Sector(double fromDegrees, double toDegrees)
    {
        const int steps = 24;
        Point At(double radius, double degrees) =>
            new(Center + radius * Math.Cos(degrees * Math.PI / 180), Center + radius * Math.Sin(degrees * Math.PI / 180));

        var figure = new PathFigure { StartPoint = At(OuterRadius, fromDegrees), IsClosed = true, IsFilled = true };
        var outline = new PolyLineSegment();
        for (var s = 1; s <= steps; s++)
            outline.Points.Add(At(OuterRadius, fromDegrees + (toDegrees - fromDegrees) * s / steps));
        for (var s = steps; s >= 0; s--)
            outline.Points.Add(At(InnerRadius, fromDegrees + (toDegrees - fromDegrees) * s / steps));
        figure.Segments.Add(outline);

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }

    private static Brush Brush(string key) => (Brush)Application.Current.Resources[key];
}
