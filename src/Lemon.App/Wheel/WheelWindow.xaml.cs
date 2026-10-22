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
/// with that preset straight away, or on the middle to just add them to the queue.
/// </summary>
public sealed partial class WheelWindow : Window
{
    private const double Diameter = 320;
    private const double Center = Diameter / 2;
    // Leaves room inside the round window for a hovered slice to pop outward.
    private const double OuterRadius = 146;
    private const double InnerRadius = 60;
    private const float PopDistance = 10;
    private const int None = -2;
    private const int CenterHit = -1;

    /// <param name="Outward">Unit vector from the wheel's center through the middle of the slice.</param>
    private sealed record Slice(Path Shape, Path Glow, StackPanel Label, Vector3 Outward);

    private readonly List<Slice> _slices = [];
    private readonly DispatcherQueueTimer _hideTimer;
    private readonly Compositor _compositor;
    private Ellipse _hub = null!;
    private StackPanel _hubContent = null!;
    private TextBlock _hubText = null!;
    private FontIcon _hubIcon = null!;
    private Image _hubImage = null!;

    private IReadOnlyList<string>? _paths;
    private Task? _loading;
    private IReadOnlyList<Preset> _choices = [];
    private int _hover = None;
    private bool _dropped;

    private int _sizePx;
    private double _regionFraction;
    private EventHandler<object>? _regionTick;
    private bool _closing;

    public WheelWindow()
    {
        InitializeComponent();

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

        _hideTimer = DispatcherQueue.CreateTimer();
        _hideTimer.IsRepeating = false;
        _hideTimer.Tick += (_, _) => Hide();

        _compositor = ElementCompositionPreview.GetElementVisual(Root).Compositor;
        ElementCompositionPreview.GetElementVisual(Ring).CenterPoint = new Vector3((float)Center, (float)Center, 0);
        BuildHub();
    }


    public nint Handle => WinRT.Interop.WindowNative.GetWindowHandle(this);

    public bool IsOpen { get; private set; }

    /// <summary>Pops the wheel up centered on a physical screen point, kept inside that monitor's work area.</summary>
    public void ShowAt(int x, int y)
    {
        _hideTimer.Stop();
        StopRegionAnimation();
        _closing = false;
        _paths = null;
        _loading = null;
        _dropped = false;
        _hover = None;
        SetChoices([]);
        SetHub("Drop on a format", icon: null);

        var monitor = Native.MonitorFromPoint(new Native.POINT { X = x, Y = y }, 2 /* MONITOR_DEFAULTTONEAREST */);
        var info = new Native.MONITORINFO { Size = System.Runtime.InteropServices.Marshal.SizeOf<Native.MONITORINFO>() };
        Native.GetMonitorInfo(monitor, ref info);
        var scale = Native.GetDpiForMonitor(monitor, 0, out var dpi, out _) == 0 ? dpi / 96.0 : 1.0;

        _sizePx = (int)Math.Ceiling(Diameter * scale);
        var work = info.WorkArea;
        var left = Math.Clamp(x - _sizePx / 2, work.Left, Math.Max(work.Left, work.Right - _sizePx));
        var top = Math.Clamp(y - _sizePx / 2, work.Top, Math.Max(work.Top, work.Bottom - _sizePx));

        AppWindow.MoveAndResize(new RectInt32(left, top, _sizePx, _sizePx));
        SetRegion(0.3);
        AppWindow.Show(activateWindow: false);
        Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        IsOpen = true;

        // Pop: the window grows out of the cursor while the ring springs open with a little twist.
        AnimateRegion(0.3, 1, TimeSpan.FromMilliseconds(240), EaseOutCubic);
        ScaleFrom(Ring, new Vector3(0.55f), Vector3.One, damping: 0.55f);
        RotateFrom(Ring, -35f, damping: 0.6f);
        RotateFrom(_hubImage, -140f, damping: 0.45f, delayMs: 60);
    }

    /// <summary>The drag ended. Give a drop on us a moment to arrive (it's processed after the release), then hide.</summary>
    public void OnDragEnded()
    {
        if (IsOpen && !_dropped)
            HideAfter(TimeSpan.FromMilliseconds(400));
    }

    /// <summary>Shrinks the wheel back into its center, then hides the window.</summary>
    public void Hide() => Close(then: null);

    private void Close(Action? then)
    {
        _hideTimer.Stop();
        if (!IsOpen || _closing)
            return;
        _closing = true;

        Spring(Ring, "Scale", new Vector3(0.8f), damping: 1f, periodMs: 30);
        AnimateRegion(_regionFraction, 0, TimeSpan.FromMilliseconds(170), EaseInCubic, () =>
        {
            AppWindow.Hide();
            IsOpen = false;
            _closing = false;
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
        if (_loading is null && e.DataView.Contains(StandardDataFormats.StorageItems))
            _loading = LoadAsync(e);
    }

    /// <summary>Reads the dragged files before the drop so the wheel can show formats that fit them.</summary>
    private async Task LoadAsync(DragEventArgs e)
    {
        var deferral = e.GetDeferral();
        try
        {
            var items = await e.DataView.GetStorageItemsAsync();
            var dragged = items.Select(i => i.Path).Where(p => !string.IsNullOrEmpty(p)).ToList();
            // A dragged folder could be huge (even a whole drive), so look inside it off the UI thread.
            _paths = await Task.Run(() => QueueViewModel.ExpandFolders(dragged).Take(5000).ToList());
        }
        catch (Exception)
        {
            // Some sources only provide virtual items (e.g. inside a zip); there's nothing on disk to convert.
            _paths = [];
        }
        finally
        {
            deferral.Complete();
        }

        try
        {
            var choices = WheelChoices(_paths);
            SetChoices(choices, animate: true);
            SetHub(
                _paths.Count == 0 ? "Lemon can't convert these"
                : choices.Count == 0 ? "Mixed file types: add to queue"
                : "Add to queue",
                icon: null);
            UpdateHover(_hover, force: true);
        }
        catch (Exception ex)
        {
            // Never leave a broken task behind for the drop to trip over; the hub still works as "add to queue".
            ErrorLog.Write("Building the conversion wheel", ex);
            SetHub("Add to queue", icon: null);
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
            return;
        StartLoading(e);

        var hit = HitTest(e.GetPosition(Ring));
        UpdateHover(hit);
        var usable = _paths is null || _paths.Count > 0;
        e.AcceptedOperation = hit != None && usable ? DataPackageOperation.Copy : DataPackageOperation.None;
        e.DragUIOverride.IsGlyphVisible = false;
        e.DragUIOverride.Caption = hit switch
        {
            >= 0 when hit < _choices.Count => "Convert to " + _choices[hit].Name,
            CenterHit when usable => "Add to Lemon",
            _ => "",
        };
    }

    private void OnDragLeave(object sender, DragEventArgs e) => UpdateHover(None);

    private async void OnDrop(object sender, DragEventArgs e)
    {
        var hit = HitTest(e.GetPosition(Ring));
        if (hit == None || (hit >= 0 && hit >= _choices.Count))
            return;

        _dropped = true;
        // Last chance to read the files if no drag event managed to (only possible for a drop on the hub).
        StartLoading(e);
        if (_loading is not null)
            await _loading;
        if (_paths is not { Count: > 0 } paths)
        {
            Hide();
            return;
        }

        if (hit >= 0)
        {
            var preset = _choices[hit];
            App.Queue.Add(paths, startWith: preset);
            Celebrate(hit);
            var files = paths.Count == 1 ? "1 file" : $"{paths.Count} files";
            SetHub(preset.IsOptimize ? $"Optimizing {files}" : $"Converting {files} to {ConversionWheel.Label(preset).Title}", icon: "îœ¾");
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

    private static IReadOnlyList<Preset> WheelChoices(IReadOnlyList<string> paths)
    {
        var supported = paths.Where(FormatCatalog.IsSupported).ToList();
        if (supported.Count == 0)
            return [];

        // Presets depend only on the file type, so check one file per extension.
        var perType = supported
            .GroupBy(p => FormatCatalog.NormalizeExtension(p))
            .Select(g => App.Queue.PresetsFor(g.First()))
            .ToList();
        var kind = supported.GroupBy(p => FormatCatalog.KindOf(p)!.Value).MaxBy(g => g.Count())!.Key;
        return ConversionWheel.Choose(perType, kind);
    }

    // ---- Hover and celebration ----

    private void UpdateHover(int hit, bool force = false)
    {
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
            foreach (var text in slice.Label.Children.OfType<TextBlock>())
                text.Foreground = Brush(on ? "TextOnAccentFillColorPrimaryBrush" : "TextFillColorPrimaryBrush");
        }

        var onHub = hit == CenterHit && !_dropped;
        _hub.Fill = Brush(onHub ? "SubtleFillColorSecondaryBrush" : "CardBackgroundFillColorDefaultBrush");
        Spring(_hub, "Scale", new Vector3(onHub ? 1.1f : 1f), damping: 0.4f);
        Spring(_hubContent, "Scale", new Vector3(onHub ? 1.12f : 1f), damping: 0.4f);

        // The lemon perks up whenever the pointer lands somewhere new.
        if (changed && hit != None && !_dropped)
            Wiggle(_hubImage);
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

    /// <summary>Animates the round window shape itself, so the wheel really grows out of / shrinks into the cursor.</summary>
    private void AnimateRegion(double from, double to, TimeSpan duration, Func<double, double> ease, Action? done = null)
    {
        StopRegionAnimation();
        var start = Stopwatch.GetTimestamp();
        EventHandler<object>? tick = null;
        tick = (_, _) =>
        {
            var t = Math.Min(1, Stopwatch.GetElapsedTime(start) / duration);
            SetRegion(from + (to - from) * ease(t));
            if (t < 1)
                return;
            StopRegionAnimation();
            done?.Invoke();
        };
        _regionTick = tick;
        CompositionTarget.Rendering += tick;
    }

    private void StopRegionAnimation()
    {
        if (_regionTick is null)
            return;
        CompositionTarget.Rendering -= _regionTick;
        _regionTick = null;
    }

    private void SetRegion(double fraction)
    {
        _regionFraction = fraction;
        var center = _sizePx / 2.0;
        var radius = Math.Max(1, center * fraction);
        Native.SetWindowRgn(Handle, Native.CreateEllipticRgn(
            (int)Math.Floor(center - radius), (int)Math.Floor(center - radius),
            (int)Math.Ceiling(center + radius) + 1, (int)Math.Ceiling(center + radius) + 1), true);
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
        Ring.Children.Add(new Ellipse
        {
            Width = OuterRadius * 2 + 6,
            Height = OuterRadius * 2 + 6,
            Stroke = Brush("AccentFillColorDefaultBrush"),
            StrokeThickness = 3,
            Margin = new Thickness(Center - OuterRadius - 3, Center - OuterRadius - 3, 0, 0),
        });

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
        _hubContent = new StackPanel { Width = InnerRadius * 2, Spacing = 4, Children = { _hubImage, _hubIcon, _hubText } };
        _hubContent.SizeChanged += (_, _) =>
        {
            Canvas.SetTop(_hubContent, Center - _hubContent.ActualHeight / 2);
            ElementCompositionPreview.GetElementVisual(_hubContent).CenterPoint =
                new Vector3((float)_hubContent.ActualWidth / 2, (float)_hubContent.ActualHeight / 2, 0);
        };
        Canvas.SetLeft(_hubContent, Center - InnerRadius);
        Canvas.SetZIndex(_hubContent, 2);
        Ring.Children.Add(_hubContent);
    }

    private void SetHub(string text, string? icon)
    {
        _hubText.Text = text;
        _hubIcon.Glyph = icon ?? "";
        _hubIcon.Visibility = icon is null ? Visibility.Collapsed : Visibility.Visible;
        _hubImage.Visibility = icon is null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetChoices(IReadOnlyList<Preset> choices, bool animate = false)
    {
        foreach (var slice in _slices)
        {
            Ring.Children.Remove(slice.Shape);
            Ring.Children.Remove(slice.Glow);
            Ring.Children.Remove(slice.Label);
        }
        _slices.Clear();
        _choices = choices;

        var sweep = 360.0 / Math.Max(1, choices.Count);
        var wheelCenter = new Vector3((float)Center, (float)Center, 0);
        for (var i = 0; i < choices.Count; i++)
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
                Fill = Brush("AccentFillColorDefaultBrush"),
                IsHitTestVisible = false,
            };

            var (title, detail) = ConversionWheel.Label(choices[i]);
            var label = new StackPanel { Width = 84, IsHitTestVisible = false };
            label.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, FontSize = 15, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            if (detail is not null)
                label.Children.Add(new TextBlock { Text = detail, FontSize = 11, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis, Opacity = 0.75 });

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

    /// <summary>A ring slice between two angles (degrees, 0 = right, clockwise), drawn as fine polylines.</summary>
    private static PathGeometry Sector(double fromDegrees, double toDegrees)
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
