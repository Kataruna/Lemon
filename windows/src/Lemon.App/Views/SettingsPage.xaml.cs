using CommunityToolkit.Mvvm.ComponentModel;
using Lemon.Core.Models;
using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Pickers;

namespace Lemon.App.Views;

public sealed record ToolStatus(ExternalTool Tool, string Title, string Purpose, string Detail, bool Found)
{
    public string Glyph => Found ? "" : "";
    public Brush Brush => (Brush)Application.Current.Resources[Found ? "SystemFillColorSuccessBrush" : "SystemFillColorCautionBrush"];
    public FontFamily DetailFont => new(Found ? "Segoe UI Variable" : "Consolas");
    /// <summary>The welcome guide's button: install with winget, or open the download page for one that isn't on it.</summary>
    public string ActionText => ToolLocator.WingetId(Tool) is null ? "Get it…" : "Add";
    public Visibility ActionVisibility => Found ? Visibility.Collapsed : Visibility.Visible;
    public Visibility ReadyVisibility => Found ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>The welcome guide names the kind of file, not the program.</summary>
    public string Kind => Tool switch
    {
        ExternalTool.Ffmpeg => "Videos and music",
        ExternalTool.Pandoc => "Documents and ebooks",
        ExternalTool.LibreOffice => "Making PDFs",
        _ => "Shrinking PDFs",
    };

    public string KindDetail => Tool switch
    {
        ExternalTool.Ffmpeg => "MP4, MP3, GIFs from videos and more",
        ExternalTool.Pandoc => "Word, EPUB, web pages and more",
        ExternalTool.LibreOffice => "Turn Word, Excel and PowerPoint files into PDFs",
        _ => "Make PDFs smaller to share",
    };

    public string KindGlyph => Tool switch
    {
        ExternalTool.Ffmpeg => "",
        ExternalTool.Pandoc => "",
        ExternalTool.LibreOffice => "",
        _ => "",
    };

    /// <summary>Looks for every converter again (picking up ones installed since) and says what each is for.</summary>
    public static ToolStatus[] CheckAll()
    {
        App.Conversion.RefreshTools();
        var locator = new ToolLocator(tool => App.Settings.ToolPaths.GetValueOrDefault(tool));
        return
        [
            Status(locator, ExternalTool.Ffmpeg, "Audio and video, video to GIF, and optimizing video"),
            Status(locator, ExternalTool.Pandoc, "Word, OpenDocument, RTF, EPUB, HTML and Markdown"),
            Status(locator, ExternalTool.LibreOffice, "PDF output, plus Excel and PowerPoint to PDF"),
            Status(locator, ExternalTool.Ghostscript, "Optimizing PDFs"),
        ];
    }

    private static ToolStatus Status(ToolLocator locator, ExternalTool tool, string purpose)
    {
        var path = locator.Find(tool);
        return path is not null
            ? new ToolStatus(tool, ToolLocator.DisplayName(tool), purpose, path, true)
            : new ToolStatus(tool, $"{ToolLocator.DisplayName(tool)} — not found", purpose, ToolLocator.InstallHint(tool), false);
    }
}

/// <summary>One downloadable AI model in Settings.</summary>
public sealed partial class ModelRow(ModelFile info) : ObservableObject
{
    private CancellationTokenSource? _cancel;

    public ModelFile Info { get; } = info;
    public string Title => $"{Info.DisplayName} — {Info.Bytes / 1_000_000} MB";
    public string Purpose => Info.Purpose;

    [ObservableProperty] public partial string Status { get; set; } = "";
    [ObservableProperty] public partial double Progress { get; set; }
    [ObservableProperty] public partial bool IsBusy { get; set; }
    [ObservableProperty] public partial string ButtonText { get; set; } = "";

    public void Refresh()
    {
        Status = IsBusy ? "Downloading…" : Info.IsInstalled ? "Downloaded" : "Not downloaded";
        ButtonText = IsBusy ? "Cancel" : Info.IsInstalled ? "Remove" : "Download";
    }

    public async Task ToggleAsync()
    {
        if (IsBusy)
        {
            _cancel?.Cancel();
            return;
        }
        if (Info.IsInstalled)
        {
            try
            {
                File.Delete(Info.Path);
                Refresh();
                UpdateContextMenu();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Status = "Couldn't remove it: " + ex.Message;
            }
            return;
        }

        _cancel = new CancellationTokenSource();
        IsBusy = true;
        Progress = 0;
        Refresh();
        try
        {
            // The window's dispatcher thread owns Progress, so report through a context-capturing Progress<T>.
            await Info.DownloadAsync(App.Http, new Progress<double>(p => Progress = p), _cancel.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or UnauthorizedAccessException)
        {
            IsBusy = false;
            Refresh();
            Status = "Download failed: " + ex.Message;
            return;
        }
        IsBusy = false;
        Refresh();
        UpdateContextMenu();
    }

    /// <summary>The right-click menu offers Remove background and Upscale only with their model downloaded.</summary>
    private static void UpdateContextMenu()
    {
        if (ContextMenu.IsRegistered())
            ContextMenu.Register(Environment.ProcessPath!, App.Conversion);
    }
}

public sealed partial class SettingsPage : Page
{
    private bool _loading;

    public SettingsPage()
    {
        InitializeComponent();
        _loading = true;
        OutputChoice.SelectedIndex = App.Settings.OutputFolder is null ? 0 : 1;
        UpdateOutputFolder();
        ParallelBox.Value = App.Settings.MaxParallelJobs;
        ContextMenuSwitch.IsOn = ContextMenu.IsRegistered();
        TraySwitch.IsOn = App.Settings.KeepRunningInTray;
        StartupSwitch.IsOn = StartWithWindows.IsEnabled();
        VersionText.Text = $"Lemon {App.Version.ToString(3)}";
        UpdateCheckBox.IsChecked = App.Settings.CheckForUpdates;
        if (App.Update is { } update)
        {
            UpdateLink.Content = $"Lemon {update.Version} is available: download it";
            UpdateLink.Visibility = Visibility.Visible;
        }
        RefreshTools();
        ModelList.ItemsSource = BackgroundModelInfo.All.Concat<ModelFile>(UpscaleModelInfo.All).Select(m => new ModelRow(m)).ToList();
        foreach (ModelRow row in (IEnumerable<ModelRow>)ModelList.ItemsSource)
            row.Refresh();
        _loading = false;
    }

    private async void OnModelAction(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is ModelRow row)
            await row.ToggleAsync();
    }

    private void UpdateOutputFolder()
    {
        var custom = OutputChoice.SelectedIndex == 1;
        CustomFolderPanel.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;
        OutputFolderText.Text = App.Settings.OutputFolder ?? "No folder chosen yet";
    }

    private async void OnOutputChoiceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading)
            return;
        if (OutputChoice.SelectedIndex == 0)
        {
            App.Settings.OutputFolder = null;
            App.Settings.Save();
        }
        else if (App.Settings.OutputFolder is null && !await PickOutputFolderAsync())
        {
            // Nothing chosen, so stay with "next to the original".
            _loading = true;
            OutputChoice.SelectedIndex = 0;
            _loading = false;
        }
        UpdateOutputFolder();
    }

    private async void OnChooseOutputFolder(object sender, RoutedEventArgs e)
    {
        await PickOutputFolderAsync();
        UpdateOutputFolder();
    }

    private static async Task<bool> PickOutputFolderAsync()
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.Window!.Handle);
        if (await picker.PickSingleFolderAsync() is not { } folder)
            return false;
        App.Settings.OutputFolder = folder.Path;
        App.Settings.Save();
        return true;
    }

    private void OnParallelChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_loading || double.IsNaN(args.NewValue))
            return;
        App.Settings.MaxParallelJobs = (int)Math.Clamp(args.NewValue, 1, 16);
        App.Settings.Save();
    }

    private void OnTrayToggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;
        App.Settings.KeepRunningInTray = TraySwitch.IsOn;
        App.Settings.Save();
    }

    private void OnStartupToggled(object sender, RoutedEventArgs e)
    {
        if (!_loading)
            App.SetStartWithWindows(StartupSwitch.IsOn);
    }

    private void OnContextMenuToggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;
        if (ContextMenuSwitch.IsOn)
            ContextMenu.Register(Environment.ProcessPath!, App.Conversion);
        else
            ContextMenu.Unregister();
    }

    private void OnRecheckTools(object sender, RoutedEventArgs e) => RefreshTools();

    private async void OnLocateTool(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ExternalTool tool)
            return;

        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".exe");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.Window!.Handle);
        if (await picker.PickSingleFileAsync() is not { } file)
            return;

        App.Settings.ToolPaths[tool] = file.Path;
        App.Settings.Save();
        RefreshTools();
    }

    private void RefreshTools()
    {
        ToolList.ItemsSource = ToolStatus.CheckAll();

        // Re-register so the right-click menu offers targets that just became possible.
        if (!_loading && ContextMenuSwitch.IsOn)
            ContextMenu.Register(Environment.ProcessPath!, App.Conversion);
    }

    private void OnDownloadUpdate(object sender, RoutedEventArgs e) => App.OpenUpdatePage();

    private void OnShowWelcome(object sender, RoutedEventArgs e) => App.Window?.ShowWelcome();

    private void OnUpdateCheckClick(object sender, RoutedEventArgs e)
    {
        App.Settings.CheckForUpdates = UpdateCheckBox.IsChecked == true;
        App.Settings.Save();
    }

    private void OnOpenSettingsFolder(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(JsonFileStore.DefaultDirectory);
        Process.Start("explorer.exe", JsonFileStore.DefaultDirectory);
    }
}
