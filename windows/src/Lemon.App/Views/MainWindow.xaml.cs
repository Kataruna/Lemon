using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Graphics;

namespace Lemon.App.Views;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "Lemon.ico"));

        var scale = Native.GetDpiForWindow(Handle) / 96.0;
        AppWindow.Resize(new SizeInt32((int)(1040 * scale), (int)(700 * scale)));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = (int)(760 * scale);
            presenter.PreferredMinimumHeight = (int)(480 * scale);
        }

        if (App.Settings.WelcomeShown)
            Nav.SelectedItem = Nav.MenuItems[0];
        else
            ShowWelcome();
        AppWindow.Closing += OnClosing;
    }

    private bool _closeConfirmed;
    private bool _quitting;

    /// <summary>Raised when the close button hid the window to the tray instead of quitting.</summary>
    public event Action? HiddenToTray;

    public nint Handle => WinRT.Interop.WindowNative.GetWindowHandle(this);

    public bool IsInForeground => AppWindow.IsVisible && Native.GetForegroundWindow() == Handle;

    private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_closeConfirmed)
            return;
        args.Cancel = true;

        if (App.Settings.KeepRunningInTray && !_quitting)
        {
            // Conversions and the wheel carry on; the tray icon brings the window back.
            AppWindow.Hide();
            HiddenToTray?.Invoke();
            return;
        }
        Quit();
    }

    /// <summary>Quits Lemon, asking first if conversions are still running.</summary>
    public async void Quit()
    {
        _quitting = true;
        if (App.Queue.IsBusy)
        {
            BringToFront();
            var dialog = new ContentDialog
            {
                XamlRoot = Content.XamlRoot,
                Title = "Conversions are still running",
                Content = "If you quit now, files that haven't finished converting won't be saved.",
                PrimaryButtonText = "Stop and quit",
                CloseButtonText = "Keep converting",
                DefaultButton = ContentDialogButton.Close,
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                _quitting = false;
                return;
            }
            await App.Queue.CancelAllAndWaitAsync();
        }
        _closeConfirmed = true;
        Close();
    }

    public void ShowConvertPage() => Nav.SelectedItem = Nav.MenuItems[0];

    public void ShowSettingsPage() => Nav.SelectedItem = Nav.SettingsItem;

    public void ShowWheelPage() => Nav.SelectedItem = Nav.MenuItems[2];

    /// <summary>The first-run guide. It isn't one of the tabs, so none is selected while it's up.</summary>
    public void ShowWelcome()
    {
        Nav.SelectedItem = null;
        ContentFrame.Navigate(typeof(WelcomePage), null, new SuppressNavigationTransitionInfo());
    }

    /// <summary>Shows the window (from the tray, minimized, or never shown yet) and focuses it.</summary>
    public void BringToFront()
    {
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
            presenter.Restore();
        AppWindow.Show();
        Activate();
        Native.SetForegroundWindow(Handle);
    }

    private void OnNavigationChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        // Cleared for the welcome guide, which navigates by itself.
        if (args.SelectedItem is null && !args.IsSettingsSelected)
            return;
        var page = args.IsSettingsSelected
            ? typeof(SettingsPage)
            : (args.SelectedItem as NavigationViewItem)?.Tag switch
            {
                "presets" => typeof(PresetsPage),
                "wheel" => typeof(WheelSettingsPage),
                _ => typeof(ConvertPage),
            };
        if (ContentFrame.CurrentSourcePageType != page)
            ContentFrame.Navigate(page, null, new SuppressNavigationTransitionInfo());
    }
}
