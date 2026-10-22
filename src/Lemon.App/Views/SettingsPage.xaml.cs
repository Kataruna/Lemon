using System.Diagnostics;
using System.Reflection;
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
        ShakeSwitch.IsOn = App.Settings.WheelOnShake;
        WheelKeyBox.ItemsSource = new[] { "Off" }.Concat(App.WheelKeys.Keys).ToList();
        WheelKeyBox.SelectedItem = App.Settings.WheelKey is { } key && App.WheelKeys.ContainsKey(key) ? key : "Off";
        VersionText.Text = $"Lemon {Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)}";
        RefreshTools();
        _loading = false;
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

    private void OnShakeToggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;
        App.Settings.WheelOnShake = ShakeSwitch.IsOn;
        App.Settings.Save();
        App.ApplyWheelSettings();
    }

    private void OnWheelKeyChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || WheelKeyBox.SelectedItem is not string key)
            return;
        App.Settings.WheelKey = key == "Off" ? null : key;
        App.Settings.Save();
        App.ApplyWheelSettings();
    }

    private void OnContextMenuToggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;
        if (ContextMenuSwitch.IsOn)
            ContextMenu.Register(Environment.ProcessPath!, (from, to) => App.Conversion.FindConverter(from, to) is not null);
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
        App.Conversion.RefreshTools();
        var locator = new ToolLocator(tool => App.Settings.ToolPaths.GetValueOrDefault(tool));
        ToolList.ItemsSource = new[]
        {
            Status(locator, ExternalTool.Ffmpeg, "Audio and video, video to GIF, and optimizing video"),
            Status(locator, ExternalTool.Pandoc, "Word, OpenDocument, RTF, EPUB, HTML and Markdown"),
            Status(locator, ExternalTool.LibreOffice, "PDF output, plus Excel and PowerPoint to PDF"),
            Status(locator, ExternalTool.Ghostscript, "Optimizing PDFs"),
        };

        // Re-register so the right-click menu offers targets that just became possible.
        if (!_loading && ContextMenuSwitch.IsOn)
            ContextMenu.Register(Environment.ProcessPath!, (from, to) => App.Conversion.FindConverter(from, to) is not null);
    }

    private static ToolStatus Status(ToolLocator locator, ExternalTool tool, string purpose)
    {
        var path = locator.Find(tool);
        return path is not null
            ? new ToolStatus(tool, ToolLocator.DisplayName(tool), purpose, path, true)
            : new ToolStatus(tool, $"{ToolLocator.DisplayName(tool)} — not found", purpose, ToolLocator.InstallHint(tool), false);
    }

    private void OnOpenSettingsFolder(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(JsonFileStore.DefaultDirectory);
        Process.Start("explorer.exe", JsonFileStore.DefaultDirectory);
    }
}
