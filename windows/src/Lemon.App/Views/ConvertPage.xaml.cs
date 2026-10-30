using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;

namespace Lemon.App.Views;

public sealed partial class ConvertPage : Page
{
    public ConvertPage()
    {
        InitializeComponent();
        // Says how the wheel opens right now, which Settings may have changed since last time.
        Loaded += (_, _) => WheelHintText.Text = $"Or {App.WheelHint} while dragging files anywhere for the wheel"
            + (App.Settings.WheelOnKey ? $", or select files in File Explorer and hold {App.KeyName}." : ".");
    }

    public QueueViewModel Queue => App.Queue;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        App.Conversion.RefreshTools();
        Queue.RefreshPresets();
        OutputFolderLink.Content = App.Settings.OutputFolder is { } folder
            ? $"Saving to {folder}"
            : "Saving next to the originals";
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
            return;
        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = "Add to Lemon";
        e.DragUIOverride.IsGlyphVisible = false;
        DropOverlay.Visibility = Queue.IsEmpty ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnDragLeave(object sender, DragEventArgs e) => DropOverlay.Visibility = Visibility.Collapsed;

    private async void OnDrop(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
            return;

        // Keep the dragged data available until we've read it.
        var deferral = e.GetDeferral();
        List<string> dropped;
        try
        {
            var items = await e.DataView.GetStorageItemsAsync();
            dropped = [.. items.Select(i => i.Path).Where(p => !string.IsNullOrEmpty(p))];
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Reading dropped files", ex);
            Queue.Notice = "Lemon couldn't read the dropped files. Try adding them with Add files instead.";
            return;
        }
        finally
        {
            deferral.Complete();
        }
        await AddAsync(dropped);
    }

    /// <summary>A folder can be huge (even a whole drive), so it's looked through off the UI thread, as the wheel does.</summary>
    private async Task AddAsync(IReadOnlyList<string> paths) =>
        Queue.Add(await Task.Run(() => QueueViewModel.ExpandFolders(paths).ToList()));

    private async void OnAddFiles(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker { ViewMode = PickerViewMode.List };
        foreach (var ext in FormatCatalog.Extensions.Order())
            picker.FileTypeFilter.Add("." + ext);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.Window!.Handle);

        var files = await picker.PickMultipleFilesAsync();
        if (files.Count > 0)
            Queue.Add(files.Select(f => f.Path));
    }

    private async void OnAddFolder(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.Window!.Handle);

        if (await picker.PickSingleFolderAsync() is { } folder)
            await AddAsync([folder.Path]);
    }

    private void OnSetAllOpening(object sender, object e)
    {
        SetAllMenu.Items.Clear();
        foreach (var (kind, presets) in Queue.PresetChoicesByKind())
        {
            var submenu = new MenuFlyoutSubItem { Text = KindLabel(kind) };
            foreach (var preset in presets)
            {
                var item = new MenuFlyoutItem { Text = preset.Name };
                item.Click += (_, _) => Queue.ApplyToAll(kind, preset);
                submenu.Items.Add(item);
            }
            SetAllMenu.Items.Add(submenu);
        }
        if (SetAllMenu.Items.Count == 0)
            SetAllMenu.Items.Add(new MenuFlyoutItem { Text = "Nothing left to change", IsEnabled = false });
    }

    private static string KindLabel(MediaKind kind) => kind switch
    {
        MediaKind.Image => "All images",
        MediaKind.Audio => "All audio",
        MediaKind.Video => "All videos",
        _ => "All documents",
    };

    private void OnRetry(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is JobViewModel job)
            Queue.Retry(job);
    }

    private void OnRemove(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is JobViewModel job)
            Queue.Remove(job);
    }

    private void OnNoticeClosed(InfoBar sender, object args) => Queue.Notice = null;

    private void OnUpdateClosed(InfoBar sender, object args) => Queue.UpdateMessage = null;

    private void OnDownloadUpdate(object sender, RoutedEventArgs e) => App.OpenUpdatePage();

    private void OnConvert(object sender, RoutedEventArgs e) => Queue.Start();

    private void OnOptimizeAll(object sender, RoutedEventArgs e) => Queue.OptimizeAll();

    private void OnCancelAll(object sender, RoutedEventArgs e) => Queue.CancelAll();

    private void OnClearFinished(object sender, RoutedEventArgs e) => Queue.ClearFinished();

    private void OnClearAll(object sender, RoutedEventArgs e) => Queue.ClearAll();

    private void OnOutputFolderClick(object sender, RoutedEventArgs e) => App.Window?.ShowSettingsPage();
}
