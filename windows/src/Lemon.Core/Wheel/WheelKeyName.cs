namespace Lemon.Core.Wheel;

/// <summary>
/// The wheel key's saved name, a Windows.System.VirtualKey name. Shift, Ctrl and Alt also come as "LeftShift",
/// "RightMenu" and so on, for one side of the keyboard only.
/// </summary>
public static class WheelKeyName
{
    /// <summary>The key without its side, or null for a key that isn't on both sides.</summary>
    public static string? Base(string? key) => key switch
    {
        "Shift" or "LeftShift" or "RightShift" => "Shift",
        "Control" or "LeftControl" or "RightControl" => "Control",
        "Menu" or "LeftMenu" or "RightMenu" => "Menu",
        _ => null,
    };

    /// <summary>"Left", "Right", or "" for either side.</summary>
    public static string Side(string? key)
    {
        if (Base(key) is null)
            return "";
        return key!.StartsWith("Left", StringComparison.Ordinal) ? "Left"
            : key.StartsWith("Right", StringComparison.Ordinal) ? "Right"
            : "";
    }

    /// <summary>The same key on that side ("Left", "Right" or "" for either).</summary>
    public static string? WithSide(string? key, string side) => Base(key) is { } name ? side + name : key;

    /// <summary>The key as people know it: Windows calls Alt "Menu".</summary>
    public static string Display(string? key)
    {
        if (Base(key) is not { } name)
            return key ?? "";
        var shown = name switch { "Menu" => "Alt", "Control" => "Ctrl", _ => name };
        return Side(key) is { Length: > 0 } side ? $"{side} {shown}" : shown;
    }
}
