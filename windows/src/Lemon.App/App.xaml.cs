using System.Reflection;
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

    /// <summary>This copy's version, e.g. 0.3.0.</summary>
    public static Version Version { get; } = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    /// <summary>A newer release found on GitHub at startup, if any.</summary>
    public static UpdateCheck.Release? Update { get; private set; }

    private static DragGestureMonitor? _gestures;
    private static TrayIcon? _tray;

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
        Queue.BatchFinished += OnBatchFinished;
        Queue.BatchFinished += _ => TrimMemoryLater();
        Queue.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(QueueViewModel.BatchProgress) or nameof(QueueViewModel.BatchStatus))
                _tray?.SetProgress(Queue.BatchProgress, Queue.BatchStatus);
        };

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

        _ = CheckForUpdatesAsync();
    }

    /// <summary>Once per launch and quietly: being offline, or GitHub having trouble, just means no notice.</summary>
    private static async Task CheckForUpdatesAsync()
    {
        if (!Settings.CheckForUpdates)
            return;
        try
        {
            if (await UpdateCheck.FindAsync(Http, Version) is not { } release)
                return;
            // Back on the UI thread here, since OnLaunched started this on it.
            Update = release;
            Queue.UpdateMessage = $"Lemon {release.Version} is available (you have {Version.ToString(3)}).";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Checking for updates", ex);
        }
    }

    /// <summary>Opens the newer release's page on GitHub, where the downloads are.</summary>
    public static void OpenUpdatePage()
    {
        if (Update is { } release)
            _ = Windows.System.Launcher.LaunchUriAsync(new Uri(release.Url));
    }

    /// <summary>How the wheel opens right now, e.g. "shake the mouse or press Alt".</summary>
    public static string WheelHint =>
        Settings.WheelOnShake && Settings.WheelOnKey ? $"shake the mouse or press {KeyName}"
        : Settings.WheelOnKey ? $"press {KeyName}"
        : "shake the mouse";

    /// <summary>The wheel key as people know it: Windows calls Alt "Menu".</summary>
    public static string KeyName => Settings.WheelKey == "Menu" ? "Alt" : Settings.WheelKey ?? "";

    /// <summary>A Windows notification from the tray icon about something that went wrong, e.g. a command that failed.</summary>
    internal static void Notify(string title, string message) => _tray?.ShowNotification(title, message, isError: true);

    public static void ApplyWheelSettings()
    {
        // Older settings turned the key off by clearing it, and could leave no way to open the wheel at all.
        if (Settings.WheelKey is null)
        {
            Settings.WheelKey = "Menu";
            Settings.WheelOnKey = false;
        }
        // Alt became the default (held, it also opens the wheel for files selected in Explorer). Once, and only for
        // the old default: a key picked by hand stays, and so does Space if it's picked again later.
        if (!Settings.WheelKeyMovedToAlt)
        {
            if (Settings.WheelKey == "Space")
                Settings.WheelKey = "Menu";
            Settings.WheelKeyMovedToAlt = true;
            Settings.Save();
        }
        if (!Settings.WheelOnShake && !Settings.WheelOnKey)
            Settings.WheelOnShake = true;
        if (_tray is not null)
            _tray.Tooltip = $"Lemon: {WheelHint} while dragging files";
        if (_gestures is null)
            return;
        _gestures.ShakeEnabled = Settings.WheelOnShake;
        _gestures.WheelKey = Settings.WheelOnKey && Enum.TryParse<Windows.System.VirtualKey>(Settings.WheelKey, out var key) ? (int)key : 0;
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
            $"Lemon: {WheelHint} while dragging files",
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
        gestures.EscapePressed += () => wheel.DispatcherQueue.TryEnqueue(wheel.OnEscape);
        gestures.PageRequested += step => wheel.DispatcherQueue.TryEnqueue(() => wheel.TurnPage(step));
        gestures.PageSelected += number => wheel.DispatcherQueue.TryEnqueue(() => wheel.GoToNumber(number));
        gestures.KeyHeld += (x, y) => wheel.DispatcherQueue.TryEnqueue(() => wheel.ShowForSelection(x, y));
        gestures.HeldKeyReleased += () => wheel.DispatcherQueue.TryEnqueue(wheel.OnHeldKeyReleased);
        gestures.HeldClick += () => wheel.DispatcherQueue.TryEnqueue(wheel.OnHeldClick);
        gestures.EnterRequested += () => wheel.DispatcherQueue.TryEnqueue(wheel.Enter);
        gestures.StepRequested += step => wheel.DispatcherQueue.TryEnqueue(() => wheel.Step(step));
        gestures.CloseRequested += () => wheel.DispatcherQueue.TryEnqueue(() => { if (wheel.IsSelectionOpen) wheel.Hide(); });
        gestures.WheelHandle = wheel.Handle;
        gestures.IsWheelOpen = () => wheel.IsOpen;
        gestures.IsSelectionWheelOpen = () => wheel.IsSelectionOpen;

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
    /// Pops up a Windows notification when a batch finishes while the window is hidden or in the background:
    /// what was done, or what failed and why. Canceled jobs aren't news, so a batch of only those says nothing.
    /// </summary>
    private static void OnBatchFinished(IReadOnlyList<JobViewModel> jobs)
    {
        if (Window is { IsInForeground: true } || _tray is null)
            return;

        var done = jobs.Where(j => j.IsDone).ToList();
        var failed = jobs.Where(j => j.IsFailed).ToList();
        if (failed.Count > 0)
        {
            var first = failed[0];
            var title = done.Count > 0 ? $"{done.Count} done, {failed.Count} failed"
                : failed.Count == 1 ? $"Couldn't convert {first.FileName}"
                : $"{failed.Count} files couldn't be converted";
            var message = failed.Count == 1 && done.Count == 0 ? first.Error ?? "Something went wrong." : $"{first.FileName}: {first.Error}";
            _tray.ShowNotification(title, message + "\nClick to open Lemon.", isError: true);
        }
        else if (done.Count > 0)
        {
            var past = QueueViewModel.BatchVerb(done) switch
            {
                "Removing background" => "Removed background from",
                "Upscaling" => "Upscaled",
                "Optimizing" => "Optimized",
                "Resizing" => "Resized",
                _ => "Converted",
            };
            var title = $"{past} {(done.Count == 1 ? done[0].FileName : $"{done.Count} files")}";
            var message = done.Count == 1 ? done[0].StatusText
                : string.IsNullOrWhiteSpace(Settings.OutputFolder) ? "Saved next to the originals." : $"Saved in {Settings.OutputFolder}.";
            _tray.ShowNotification(title, message + "\nClick to open Lemon.", isError: false);
        }
    }

    private static DispatcherQueueTimer? _trim;

    /// <summary>
    /// Hands the memory a batch used back to Windows once it's over and the AI models have been let go (30 s after
    /// their last job). Idle in the tray, Lemon allocates next to nothing, so the GC would never run on its own and
    /// kept a few hundred MB committed (measured: 394 MB private after upscaling, 182 MB after this).
    /// </summary>
    private static void TrimMemoryLater()
    {
        if (_trim is null)
        {
            _trim = DispatcherQueue.GetForCurrentThread().CreateTimer();
            _trim.IsRepeating = false;
            _trim.Interval = TimeSpan.FromSeconds(40);
            _trim.Tick += (_, _) =>
            {
                if (!Queue.IsBusy)
                    GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            };
        }
        _trim.Stop();
        _trim.Start();
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
