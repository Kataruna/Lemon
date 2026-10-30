using Lemon.Core.Wheel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Lemon.App.Views;

/// <summary>How the conversion wheel opens: shaking, a key, or both (never neither), and which key.</summary>
public sealed partial class WheelTriggers : UserControl
{
    private bool _loading;

    /// <summary>A switch or the key changed (and was saved).</summary>
    public event Action? Changed;

    public WheelTriggers()
    {
        InitializeComponent();
        _loading = true;
        ShakeSwitch.IsOn = App.Settings.WheelOnShake;
        KeySwitch.IsOn = App.Settings.WheelOnKey;
        ShowWheelKey();
        _loading = false;
    }

    private void OnShakeToggled(object sender, RoutedEventArgs e) => OnTriggerToggled(ShakeSwitch, KeySwitch, "pressing a key");

    private void OnKeyToggled(object sender, RoutedEventArgs e) => OnTriggerToggled(KeySwitch, ShakeSwitch, "shaking the mouse");

    /// <summary>Either way of opening the wheel can be off, but not both: turning off the last one turns the other back on.</summary>
    private void OnTriggerToggled(ToggleSwitch changed, ToggleSwitch other, string otherName)
    {
        if (_loading)
            return;
        TriggerInfo.IsOpen = false;
        if (!changed.IsOn && !other.IsOn)
        {
            _loading = true;
            other.IsOn = true;
            _loading = false;
            TriggerInfo.Message = $"You can't turn off both ways to open the wheel, so opening it by {otherName} is back on.";
            TriggerInfo.IsOpen = true;
        }
        App.Settings.WheelOnShake = ShakeSwitch.IsOn;
        App.Settings.WheelOnKey = KeySwitch.IsOn;
        App.Settings.Save();
        App.ApplyWheelSettings();
        ShowWheelKey();
        Changed?.Invoke();
    }

    // ---- Wheel key: click the button, then press the key ----

    private bool _recordingKey;

    private void ShowWheelKey()
    {
        _recordingKey = false;
        WheelKeyButton.Content = App.KeyName;
        WheelKeyButton.IsEnabled = App.Settings.WheelOnKey;
        KeySide.Visibility = WheelKeyName.Base(App.Settings.WheelKey) is null ? Visibility.Collapsed : Visibility.Visible;
        KeySide.IsEnabled = App.Settings.WheelOnKey;
        KeySide.SelectedIndex = WheelKeyName.Side(App.Settings.WheelKey) switch { "Left" => 1, "Right" => 2, _ => 0 };
    }

    private void OnKeySideChanged(object sender, SelectionChangedEventArgs e)
    {
        var side = KeySide.SelectedIndex switch { 1 => "Left", 2 => "Right", _ => "" };
        var key = WheelKeyName.WithSide(App.Settings.WheelKey, side);
        if (key == App.Settings.WheelKey)
            return;
        App.Settings.WheelKey = key;
        App.Settings.Save();
        App.ApplyWheelSettings();
        ShowWheelKey();
        Changed?.Invoke();
    }

    private void OnWheelKeyClick(object sender, RoutedEventArgs e)
    {
        _recordingKey = true;
        WheelKeyButton.Content = "Press a key…";
    }

    private void OnWheelKeyLostFocus(object sender, RoutedEventArgs e)
    {
        if (_recordingKey)
            ShowWheelKey();
    }

    private void OnWheelKeyPressed(object sender, KeyRoutedEventArgs e)
    {
        if (!_recordingKey)
            return;
        // Otherwise Tab would move focus and Space / Enter would click the button again.
        e.Handled = true;
        if (e.Key == VirtualKey.Escape)
        {
            ShowWheelKey();
            return;
        }
        if (!CanOpenWheel(e.Key))
        {
            WheelKeyButton.Content = "Pick another key…";
            return;
        }
        // Pressing either one picks the key; the side has its own list, because e.Key doesn't always say which was pressed.
        App.Settings.WheelKey = WheelKeyName.Base(e.Key.ToString()) ?? e.Key.ToString();
        App.Settings.Save();
        App.ApplyWheelSettings();
        ShowWheelKey();
        Changed?.Invoke();
    }

    /// <summary>
    /// Esc cancels the drag and the number keys pick rings. Alt, Shift and Ctrl work (Alt is the default; mid-drag the
    /// hook swallows Ctrl, so it doesn't turn the drag into a copy); Win opens Start, so it can't be the wheel key.
    /// </summary>
    private static bool CanOpenWheel(VirtualKey key) =>
        key is not (VirtualKey.Escape
            or VirtualKey.LeftWindows or VirtualKey.RightWindows
            or (>= VirtualKey.Number0 and <= VirtualKey.Number9));
}
