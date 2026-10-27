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
        // The left and right ones are the same key to the wheel.
        App.Settings.WheelKey = e.Key switch
        {
            VirtualKey.LeftMenu or VirtualKey.RightMenu => nameof(VirtualKey.Menu),
            VirtualKey.LeftShift or VirtualKey.RightShift => nameof(VirtualKey.Shift),
            _ => e.Key.ToString(),
        };
        App.Settings.Save();
        App.ApplyWheelSettings();
        ShowWheelKey();
        Changed?.Invoke();
    }

    /// <summary>
    /// Esc cancels the drag and the number keys pick rings. Alt and Shift work (Alt is the default); Ctrl copies a drag
    /// and Win opens Start, so those two can't be the wheel key.
    /// </summary>
    private static bool CanOpenWheel(VirtualKey key) =>
        key is not (VirtualKey.Escape
            or VirtualKey.Control or VirtualKey.LeftControl or VirtualKey.RightControl
            or VirtualKey.LeftWindows or VirtualKey.RightWindows
            or (>= VirtualKey.Number0 and <= VirtualKey.Number9));
}
