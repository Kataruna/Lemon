using System.ComponentModel;
using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.System;
using Windows.UI.ViewManagement;

namespace Lemon.App.Views;

/// <summary>
/// The first-run guide: which kinds of files to set up (each one installs a converter with winget), then the wheel.
/// Shown once; Settings › About brings it back.
/// </summary>
public sealed partial class WelcomePage : Page
{
    public WelcomePage()
    {
        InitializeComponent();
        ToolList.ItemsSource = ToolStatus.CheckAll();
        ShowSummonText();
        ShowDemoWheel();
        ShowStep(0);
    }

    /// <summary>
    /// The user's own Convert ring for pictures, in their theme, with the drag aimed at its JPG slice (the first slice
    /// if JPG isn't on it); the new file takes that format's name.
    /// </summary>
    private void ShowDemoWheel()
    {
        var ring = ConversionWheel.Choose([App.Queue.PresetsFor("Photo.heic")], MediaKind.Image, WheelRing.BuiltIns[0],
            App.Settings.Wheel.ConvertRing.GetValueOrDefault(MediaKind.Image));
        var lit = Math.Max(0, ring.ToList().FindIndex(p => p.TargetExtension == "jpg"));
        WheelDrawing.Child = WheelPreview.RenderDemo(ring, lit: -1);
        LitSlice.Child = WheelPreview.RenderDemo(ring, lit);
        var extension = ring.Count > 0 ? FormatCatalog.NormalizeExtension(ring[lit].TargetExtension) : "jpg";
        ResultName.Text = "Photo." + extension;
        ShowFileIcons(extension);

        // The 180-wide wheel is the real one scaled down, centered where the pointer stops (280, 10 from where it starts).
        var angle = (lit * 360.0 / Math.Max(1, ring.Count) - 90) * Math.PI / 180;
        var radius = (WheelWindow.InnerRadius + WheelWindow.OuterRadius) / 2 * 180 / WheelWindow.Diameter;
        AimX.Value = 280 + radius * Math.Cos(angle);
        AimY.Value = 10 + radius * Math.Sin(angle);
    }

    /// <summary>The files in the demo get the icons Windows shows for them, from empty stand-ins in the temp folder.</summary>
    private async void ShowFileIcons(string resultExtension)
    {
        try
        {
            var folder = Directory.CreateDirectory(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Lemon", "Icons")).FullName;
            OriginalIcon.Source = DraggedIcon.Source = await IconAsync(folder, "heic");
            ResultIcon.Source = await IconAsync(folder, resultExtension);
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Drawing the welcome guide's file icons", ex);
        }
    }

    private static async Task<BitmapImage> IconAsync(string folder, string extension)
    {
        var path = System.IO.Path.Combine(folder, "Photo." + extension);
        if (!File.Exists(path))
            await File.WriteAllBytesAsync(path, []);
        var file = await StorageFile.GetFileFromPathAsync(path);
        using var icon = await file.GetThumbnailAsync(ThumbnailMode.ListView, 32, ThumbnailOptions.UseCurrentScale);
        var image = new BitmapImage();
        await image.SetSourceAsync(icon);
        return image;
    }

    /// <summary>Leaving the guide any way at all (Skip, a button, a tab) counts as having seen it.</summary>
    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        WheelDemo.Stop();
        if (App.Settings.WelcomeShown)
            return;
        App.Settings.WelcomeShown = true;
        App.Settings.Save();
    }

    private void ShowStep(int step)
    {
        var tools = step == 0;
        ToolsStep.Visibility = tools ? Visibility.Visible : Visibility.Collapsed;
        WheelStep.Visibility = tools ? Visibility.Collapsed : Visibility.Visible;
        HeadingText.Text = tools ? "Welcome to Lemon" : "Convert without opening Lemon";
        SubheadingText.Text = tools
            ? "Pictures work right away. What else do you want to convert?"
            : "Lemon pops up a wheel of formats while you drag files, anywhere in Windows.";
        NextButton.Visibility = ToolsStep.Visibility;
        if (tools)
            WheelDemo.Stop();
        else if (new UISettings().AnimationsEnabled)
            WheelDemo.Begin();
        else
        {
            // Animations off in Windows: a still of the open wheel and the new file instead.
            Wheel.Opacity = ResultFile.Opacity = LitSlice.Opacity = 1;
            WheelDrawing.Opacity = 0;
            WheelScale.ScaleX = WheelScale.ScaleY = 1;
        }
        BackButton.Visibility = CustomizeButton.Visibility = FinishButton.Visibility = WheelStep.Visibility;
        Pips.SelectedPageIndex = step;
    }

    /// <summary>The middle step, acted out for how the wheel opens right now: a shake, or the key popping up.</summary>
    private void ShowSummonText()
    {
        var shake = App.Settings.WheelOnShake;
        SummonLabel.Text = shake ? "2. Shake" : $"2. Press {App.Settings.WheelKey}";
        KeyCapText.Text = App.Settings.WheelKey ?? "";
        WheelDemo.Children.Remove(shake ? KeyAnimation : ShakeAnimation);
    }

    private async void OnToolAction(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ExternalTool tool } button)
            return;
        ToolInfo.IsOpen = false;
        if (ToolLocator.WingetId(tool) is not { } id)
        {
            if (ToolLocator.DownloadPage(tool) is { } page)
                await Launcher.LaunchUriAsync(new Uri(page));
            return;
        }

        button.IsEnabled = false;
        button.Content = "Adding…";
        try
        {
            // In a console window of its own, so the progress and any permission prompt show. If winget fails, the
            // window stays open on its error until a key is pressed.
            using var process = Process.Start(new ProcessStartInfo("cmd.exe",
                $"/c winget install --id {id} --exact --source winget --accept-source-agreements --accept-package-agreements || pause")
            {
                UseShellExecute = true,
            });
            if (process is not null)
                await process.WaitForExitAsync();
        }
        catch (Win32Exception)
        {
            ToolInfo.Message = "Lemon couldn't start the installer. You can set it up later from Settings › Converters.";
            ToolInfo.IsOpen = true;
            button.IsEnabled = true;
            button.Content = "Add";
            return;
        }
        var tools = ToolStatus.CheckAll();
        ToolList.ItemsSource = tools;
        if (tools.FirstOrDefault(s => s.Tool == tool) is { Found: false })
        {
            ToolInfo.Message = "That didn't finish setting up. You can try again, or later from Settings › Converters.";
            ToolInfo.IsOpen = true;
        }
    }

    private void OnNext(object sender, RoutedEventArgs e) => ShowStep(1);

    private void OnBack(object sender, RoutedEventArgs e) => ShowStep(0);

    private void OnPipsChanged(PipsPager sender, PipsPagerSelectedIndexChangedEventArgs e)
    {
        ShowStep(sender.SelectedPageIndex);
    }

    private void OnCustomizeWheel(object sender, RoutedEventArgs e) => App.Window?.ShowWheelPage();

    private void OnFinish(object sender, RoutedEventArgs e) => App.Window?.ShowConvertPage();
}
