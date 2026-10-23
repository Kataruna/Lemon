using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Lemon.App.Wheel;

/// <summary>
/// A see-through window background, so the wheel can draw its own anti-aliased round edge instead of relying on
/// the window region's jagged one. The same trick as WinUIEx's TransparentTintBackdrop.
/// </summary>
internal sealed class TransparentBackdrop : SystemBackdrop
{
    private static Windows.UI.Composition.Compositor? _compositor;

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(target, xamlRoot);
        // The backdrop takes a system (Windows.UI) brush, whose compositor needs a system dispatcher queue.
        if (_compositor is null)
        {
            if (Windows.System.DispatcherQueue.GetForCurrentThread() is null)
                Native.CreateDispatcherQueueController(new Native.DispatcherQueueOptions
                {
                    Size = System.Runtime.InteropServices.Marshal.SizeOf<Native.DispatcherQueueOptions>(),
                    ThreadType = 2, // DQTYPE_THREAD_CURRENT
                }, out _);
            _compositor = new Windows.UI.Composition.Compositor();
        }
        target.SystemBackdrop = _compositor.CreateColorBrush(Microsoft.UI.Colors.Transparent);
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop target)
    {
        target.SystemBackdrop = null;
        base.OnTargetDisconnected(target);
    }
}
