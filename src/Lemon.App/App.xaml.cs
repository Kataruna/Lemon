using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Windows.ApplicationModel.Activation;

namespace Lemon.App;

public partial class App : Application
{
    private static readonly Lock Gate = new();
    private static readonly List<LaunchRequest> PendingLaunches = [];

    public static AppSettings Settings { get; } = AppSettings.Load();
    public static PresetStore Presets { get; } = new();
    public static ConversionService Conversion { get; } = new(new ToolLocator(tool => Settings.ToolPaths.GetValueOrDefault(tool)));
    public static HttpClient Http { get; } = new();
    public static QueueViewModel Queue { get; private set; } = null!;
    public static MainWindow? Window { get; private set; }
    public static WheelWindow? Wheel { get; private set; }

    /// <summary>Keys that can open the conversion wheel, by settings name.</summary>
    public static IReadOnlyDictionary<string, int> WheelKeys { get; } = new Dictionary<string, int>
    {
        ["Space"] = 0x20,
        ["Tab"] = 0x09,
        ["F1"] = 0x70,
        ["F2"] = 0x71,
        ["F3"] = 0x72,
        ["F4"] = 0x73,
    };

    private static DragGestureMonitor? _gestures;
    private static TrayIcon? _tray;
    private static readonly List<JobViewModel> UnreportedFailures = [];
    private static DispatcherQueueTimer? _failureTimer;

    internal static LaunchRequest InitialLaunch { get; set; } = LaunchRequest.Empty;

    public App()
    {
        InitializeComponent();

        // A bug in one event handler shouldn't take the whole app (and its running conversions) down:
        // record it and carry on.
        UnhandledException += (_, e) =>
        {
            ErrorLog.Write("Unhandled UI exception", e.Exception);
            e.Handled = true;
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            ErrorLog.Write("Unobserved task exception", e.Exception);
            e.SetObserved();
        };
    }

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        Queue = new QueueViewModel(Settings, Presets, Conversion);
        Queue.JobFailed += OnJobFailed;

        var window = new MainWindow();
        StartTray(window);
        StartConversionWheel(window);
        window.Closed += (_, _) =>
        {
            // Hidden helper windows would otherwise keep the process alive after the main window closes.
            _gestures?.Dispose();
            _tray?.Dispose();
            Wheel?.Close();
        };

        List<LaunchRequest> pending;
        lock (Gate)
        {
            Window = window;
            pending = [InitialLaunch, .. PendingLaunches];
            PendingLaunches.Clear();
        }
        foreach (var launch in pending)
            Open(launch);

        // Starting with Windows or a right-click "To PNG" works from the tray; anything else shows the window.
        if (pending.Any(p => !p.StaysInBackground))
            window.Activate();
    }

    public static void ApplyWheelSettings()
    {
        if (_gestures is null)
            return;
        _gestures.ShakeEnabled = Settings.WheelOnShake;
        _gestures.WheelKey = Settings.WheelKey is { } key ? WheelKeys.GetValueOrDefault(key) : 0;
    }

    public static void SetStartWithWindows(bool enabled)
    {
        if (enabled)
            StartWithWindows.Enable(Environment.ProcessPath!);
        else
            StartWithWindows.Disable();
    }

    private static void StartTray(MainWindow window)
    {
        var tray = new TrayIcon(
            Path.Combine(AppContext.BaseDirectory, "Assets", "Lemon.ico"),
            "Lemon: shake the mouse or press Space while dragging files",
            () =>
            [
                new("Open Lemon", ShowQueue, IsDefault: true),
                null,
                new("Start with Windows", () => SetStartWithWindows(!StartWithWindows.IsEnabled()), IsChecked: StartWithWindows.IsEnabled()),
                null,
                new("Quit Lemon", window.Quit),
            ]);
        tray.Clicked += ShowQueue;
        tray.NotificationClicked += ShowQueue;
        window.HiddenToTray += () =>
        {
            if (Settings.TrayNoticeShown)
                return;
            Settings.TrayNoticeShown = true;
            Settings.Save();
            tray.ShowNotification(
                "Lemon is still running",
                "The conversion wheel keeps working while you drag files. Click the lemon in the notification area to open Lemon, or right-click it to quit.",
                isError: false);
        };
        _tray = tray;
    }

    private static void StartConversionWheel(MainWindow window)
    {
        var wheel = new WheelWindow();
        var gestures = new DragGestureMonitor();
        // Hook callbacks arrive on the hook thread; hand everything to the UI thread right away.
        gestures.Triggered += (x, y) => wheel.DispatcherQueue.TryEnqueue(() => wheel.ShowAt(x, y));
        gestures.ButtonReleased += () => wheel.DispatcherQueue.TryEnqueue(wheel.OnDragEnded);
        gestures.EscapePressed += () => wheel.DispatcherQueue.TryEnqueue(wheel.Hide);
        gestures.PageRequested += step => wheel.DispatcherQueue.TryEnqueue(() => wheel.TurnPage(step));
        gestures.PageSelected += page => wheel.DispatcherQueue.TryEnqueue(() => wheel.GoToPage(page));
        gestures.IsWheelOpen = () => wheel.IsOpen;

        Wheel = wheel;
        _gestures = gestures;
        ApplyWheelSettings();
        gestures.Start();
    }

    private static void ShowQueue()
    {
        Window?.ShowConvertPage();
        Window?.BringToFront();
    }

    /// <summary>
    /// Pops up a Windows notification for failures the user can't see (window hidden or in the background).
    /// Failures that happen close together, e.g. in one batch, are reported together.
    /// </summary>
    private static void OnJobFailed(JobViewModel job)
    {
        if (Window is { IsInForeground: true })
            return;

        UnreportedFailures.Add(job);
        if (_failureTimer is null)
        {
            _failureTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
            _failureTimer.Interval = TimeSpan.FromSeconds(1.5);
            _failureTimer.IsRepeating = false;
            _failureTimer.Tick += (_, _) => ReportFailures();
        }
        _failureTimer.Start();
    }

    private static void ReportFailures()
    {
        var failures = UnreportedFailures.ToList();
        UnreportedFailures.Clear();
        if (failures.Count == 0 || _tray is null)
            return;

        var first = failures[0];
        var title = failures.Count == 1
            ? $"Couldn't convert {first.FileName}"
            : $"{failures.Count} files couldn't be converted";
        var message = failures.Count == 1
            ? first.Error ?? "Something went wrong."
            : $"{first.FileName}: {first.Error}";
        _tray.ShowNotification(title, message + "\nClick to open Lemon.", isError: true);
    }

    /// <summary>Called on a background thread when another Lemon process forwards its launch to us.</summary>
    internal static void HandleActivation(AppActivationArguments activation)
    {
        if (activation.Kind != ExtendedActivationKind.Launch || activation.Data is not ILaunchActivatedEventArgs launch)
            return;

        // Unpackaged apps receive the whole command line, including the exe path.
        var argv = Native.SplitCommandLine(launch.Arguments).AsEnumerable();
        if (argv.FirstOrDefault() is { } first && first.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            argv = argv.Skip(1);
        var request = LaunchRequest.Parse(argv);

        lock (Gate)
        {
            if (Window is null)
            {
                PendingLaunches.Add(request);
                return;
            }
        }
        Window.DispatcherQueue.TryEnqueue(() =>
        {
            Open(request);
            // Opening Lemon again (Start menu, or "Open in Lemon…") shows the window, even from the tray.
            if (!request.StaysInBackground)
                Window.BringToFront();
        });
    }

    private static void Open(LaunchRequest request)
    {
        if (request.IsEmpty)
            return;
        Window?.ShowConvertPage();
        Queue.Add(request.Files, request.QuickTarget);
    }
}
