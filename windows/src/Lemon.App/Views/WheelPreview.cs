using ImageMagick;
using ImageMagick.Drawing;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using static Lemon.App.Wheel.WheelWindow;
using Path = Microsoft.UI.Xaml.Shapes.Path;
using TextAlignment = Microsoft.UI.Xaml.TextAlignment;

namespace Lemon.App.Views;

/// <summary>
/// A still drawing of the wheel for Settings, built from the same settings, rings and geometry as the real one: the
/// ring a kind of file opens on (or the hub), its first slice lit, the mode bar, and the glass over a sample
/// background so the blur shows.
/// </summary>
internal static class WheelPreview
{
    /// <summary>The sample background, sharp and per blur level, made once.</summary>
    private static readonly Dictionary<int, MagickImage> Backgrounds = [];

    public static FrameworkElement Render(MediaKind kind, string sample)
    {
        var settings = App.Settings.Wheel;
        var look = WheelLook.Current;
        var plan = ConversionWheel.Plan([(kind, App.Queue.PresetsFor(sample))], kind, settings);
        var inHub = plan.OpensOnHub;
        var labels = inHub
            ? plan.Rings.Select(r => (r.Name, (string?)r.Detail, (string?)r.Glyph)).ToList()
            : plan.Rings.Count == 0 ? []
            : plan.Choices[plan.Start].Select(p => ConversionWheel.Label(p) is var (title, detail) ? (title, detail, (string?)null) : default).ToList();
        // Inside a ring with the hub on, the middle leads back to it unless a chip under the ring does.
        var centerBack = plan.HubOn && !inHub && !settings.BackChip;
        int[] chips = plan.Rings.Count < 2 || inHub || centerBack ? [] : plan.HubOn ? [-1] : [.. Enumerable.Range(0, plan.Rings.Count)];
        var barWidth = chips.Length * ChipWidth + 2 * BarPadding;
        var barHeight = ChipHeight + 2 * BarPadding;

        var root = new Grid { Width = Diameter, Height = WindowHeight };
        var canvas = new Canvas { Width = Diameter, Height = WindowHeight };
        root.Children.Add(canvas);

        // What's "behind" the wheel, so the opacity and blur settings show.
        var sharp = Background(0);
        canvas.Children.Add(new Rectangle { Width = Diameter, Height = WindowHeight, RadiusX = 12, RadiusY = 12, Fill = Glass.Crop(sharp, 0, 0, (int)Diameter, (int)WindowHeight) });

        // Glass: the blurred picture under the tinted disc, for the disc and the bar alike.
        void AddGlass(Shape frost, Shape tint, double left, double top, double width, double height)
        {
            foreach (var shape in new[] { frost, tint })
            {
                shape.Width = width;
                shape.Height = height;
                Canvas.SetLeft(shape, left);
                Canvas.SetTop(shape, top);
                canvas.Children.Add(shape);
            }
            if (look.Blur > 0)
            {
                frost.Fill = Glass.Crop(Background(look.Blur), (int)left, (int)top, (int)width, (int)height);
                frost.Opacity = look.GlassOpacity;
            }
            tint.Fill = look.Disc;
        }
        AddGlass(new Ellipse(), new Ellipse(), 2, 2, Diameter - 4, Diameter - 4);
        if (chips.Length > 0)
            AddGlass(new Rectangle { RadiusX = barHeight / 2, RadiusY = barHeight / 2 }, new Rectangle { RadiusX = barHeight / 2, RadiusY = barHeight / 2 },
                Center - barWidth / 2, BarTop, barWidth, barHeight);

        var outline = new Ellipse { Width = OuterRadius * 2 + 6, Height = OuterRadius * 2 + 6, Stroke = look.AccentBrush, StrokeThickness = 3 };
        Canvas.SetLeft(outline, Center - OuterRadius - 3);
        Canvas.SetTop(outline, Center - OuterRadius - 3);
        canvas.Children.Add(outline);

        var sweep = 360.0 / Math.Max(1, labels.Count);
        for (var i = 0; i < labels.Count; i++)
        {
            var middle = i * sweep - 90;
            var radians = middle * Math.PI / 180;
            var lit = i == 0;
            canvas.Children.Add(new Path
            {
                Data = Sector(middle - sweep / 2, middle + sweep / 2),
                Fill = lit ? look.Highlight : null,
                Stroke = Brush("DividerStrokeColorDefaultBrush"),
                StrokeThickness = 1,
            });
            var (title, detail, glyph) = labels[i];
            var text = lit ? look.OnAccent : look.Text;
            var label = new StackPanel { Width = 84 };
            if (glyph is not null)
                label.Children.Add(new FontIcon { Glyph = glyph, FontSize = 16, Foreground = text, Margin = new Thickness(0, 0, 0, 2) });
            label.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, FontSize = 15, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = text });
            if (detail is not null)
                label.Children.Add(new TextBlock { Text = detail, FontSize = 11, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis, Opacity = 0.75, Foreground = text });
            var labelRadius = (InnerRadius + OuterRadius) / 2;
            Canvas.SetLeft(label, Center + labelRadius * Math.Cos(radians) - label.Width / 2);
            label.SizeChanged += (_, _) => Canvas.SetTop(label, Center + labelRadius * Math.Sin(radians) - label.ActualHeight / 2);
            canvas.Children.Add(label);
        }

        var hub = new Ellipse
        {
            Width = InnerRadius * 2,
            Height = InnerRadius * 2,
            Fill = Brush("CardBackgroundFillColorDefaultBrush"),
            Stroke = Brush("CardStrokeColorDefaultBrush"),
            StrokeThickness = 1,
        };
        Canvas.SetLeft(hub, Center - InnerRadius);
        Canvas.SetTop(hub, Center - InnerRadius);
        canvas.Children.Add(hub);
        var middleContent = new StackPanel { Width = InnerRadius * 2, Spacing = 4 };
        middleContent.Children.Add(centerBack
            ? new FontIcon { Glyph = "", FontSize = 26, Width = 32, Height = 32, Foreground = look.Text }
            : look.UsesLemonPicture
            ? new Image { Width = 32, Height = 32, Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri("ms-appx:///Assets/Lemon.png")) }
            : new TextBlock { Text = look.Center, FontSize = 28, FontFamily = new FontFamily("Segoe UI Emoji"), HorizontalAlignment = HorizontalAlignment.Center });
        middleContent.Children.Add(new TextBlock { Text = plan.Rings.Count == 0 ? "Nothing for these files" : centerBack ? "All rings" : "Add to queue", FontSize = 12, TextAlignment = TextAlignment.Center, Foreground = Brush("TextFillColorSecondaryBrush") });
        Canvas.SetLeft(middleContent, Center - InnerRadius);
        middleContent.SizeChanged += (_, _) => Canvas.SetTop(middleContent, Center - middleContent.ActualHeight / 2);
        canvas.Children.Add(middleContent);

        if (chips.Length > 0)
        {
            var bar = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var target in chips)
            {
                var current = target == plan.Start;
                var foreground = current ? look.OnAccent : look.Text;
                bar.Children.Add(new Border
                {
                    Width = ChipWidth,
                    Height = ChipHeight,
                    CornerRadius = new CornerRadius(ChipHeight / 2),
                    Background = current ? look.Highlight : null,
                    Child = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 5,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        Children =
                        {
                            new TextBlock { Text = target < 0 ? "0" : (target + 1).ToString(), FontSize = 9, FontWeight = FontWeights.Bold, Foreground = foreground },
                            new TextBlock { Text = target < 0 ? "All rings" : plan.Rings[target].Name, FontSize = 11, MaxWidth = ChipWidth - 26, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = foreground },
                        },
                    },
                });
            }
            Canvas.SetLeft(bar, Center - barWidth / 2 + BarPadding);
            Canvas.SetTop(bar, BarTop + BarPadding);
            canvas.Children.Add(bar);
        }
        return root;
    }

    private static MagickImage Background(int blur)
    {
        if (Backgrounds.TryGetValue(blur, out var cached))
            return cached;
        MagickImage image;
        if (blur == 0)
        {
            // Bright shapes on a gradient: busy enough that blur, tint and opacity all visibly change it.
            image = new MagickImage("gradient:#3A7BD5-#F2994A", new MagickReadSettings { Width = (uint)Diameter, Height = (uint)WindowHeight });
            var shapes = new Drawables().StrokeWidth(0);
            var random = new Random(7);
            string[] colors = ["#FFFFFF", "#1B1B1B", "#E94B3C", "#2ECC71", "#F1C40F", "#9B59B6"];
            for (var i = 0; i < 40; i++)
            {
                var x = random.Next((int)Diameter);
                var y = random.Next((int)WindowHeight);
                var size = random.Next(8, 40);
                shapes.FillColor(new MagickColor(colors[i % colors.Length])).Rectangle(x, y, x + size * 2, y + size / 2);
            }
            shapes.Draw(image);
            image.Alpha(AlphaOption.Off);
        }
        else
        {
            image = Glass.Blur((MagickImage)Background(0).Clone(), blur, 1);
        }
        return Backgrounds[blur] = image;
    }

    private static Brush Brush(string key) => (Brush)Application.Current.Resources[key];
}
