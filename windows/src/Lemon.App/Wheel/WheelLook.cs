using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Lemon.App.Wheel;

/// <summary>The wheel's colors, worked out from <see cref="WheelSettings"/> and the current light / dark theme.</summary>
internal sealed class WheelLook
{
    private WheelLook(WheelSettings settings)
    {
        var system = settings.Accent is null;
        Accent = system ? ((SolidColorBrush)Resource("AccentFillColorDefaultBrush")).Color : Parse(settings.Accent!);
        // The highlight runs from the accent at the top to a darker shade at the bottom, like the macOS wheel.
        Highlight = new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0.5, 0),
            EndPoint = new Windows.Foundation.Point(0.5, 1),
            GradientStops = { new GradientStop { Color = Accent, Offset = 0 }, new GradientStop { Color = Darker(Accent), Offset = 1 } },
        };
        AccentBrush = new SolidColorBrush(Accent);
        // Windows' own accent comes with a matching text color; a picked one gets black or white, whichever reads.
        OnAccent = system ? (Brush)Resource("TextOnAccentFillColorPrimaryBrush")
            : new SolidColorBrush(Luminance(Accent) > 0.6 ? Colors.Black : Colors.White);
        Text = settings.TextColor is { } text ? new SolidColorBrush(Parse(text)) : (Brush)Resource("TextFillColorPrimaryBrush");
        var baseColor = ((SolidColorBrush)Resource("SolidBackgroundFillColorBaseBrush")).Color;
        Blur = Math.Clamp(settings.Blur, 0, Glass.Names.Length - 1);
        GlassOpacity = Math.Clamp(settings.Opacity, 0.3, 1);
        // With blur, the tinted color only partly covers the blurred picture, and the opacity applies to both.
        Disc = new SolidColorBrush(Mix(baseColor, Accent, Math.Clamp(settings.Tint, 0, 0.5))) { Opacity = GlassOpacity * Glass.TintCover(Blur) };
        Center = settings.Center;
    }

    /// <summary>0 none, 1 light, 2 standard, 3 heavy.</summary>
    public int Blur { get; }

    /// <summary>Of the whole glass: the blurred picture and the tint over it.</summary>
    public double GlassOpacity { get; }

    public static WheelLook Current => new(App.Settings.Wheel);

    public Color Accent { get; }
    public Brush AccentBrush { get; }
    public Brush Highlight { get; }
    public Brush OnAccent { get; }
    public Brush Text { get; }
    public Brush Disc { get; }

    /// <summary>The emoji in the middle; Lemon's own lemon picture stands in for 🍋.</summary>
    public string Center { get; }
    public bool UsesLemonPicture => string.IsNullOrWhiteSpace(Center) || Center == "🍋";

    public static Color Parse(string hex)
    {
        var value = Convert.ToUInt32(hex.TrimStart('#'), 16);
        return Color.FromArgb(255, (byte)(value >> 16), (byte)(value >> 8), (byte)value);
    }

    public static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    private static Color Darker(Color c) => Mix(c, Colors.Black, 0.25);

    private static Color Mix(Color from, Color to, double amount) => Color.FromArgb(
        255,
        (byte)Math.Round(from.R + (to.R - from.R) * amount),
        (byte)Math.Round(from.G + (to.G - from.G) * amount),
        (byte)Math.Round(from.B + (to.B - from.B) * amount));

    private static double Luminance(Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255;

    private static object Resource(string key) => Application.Current.Resources[key];
}
