using Lemon.Core.Models;
using ImageMagick;
using ImageMagick.Drawing;

namespace Lemon.Core.Tests;

/// <summary>Needs the BiRefNet Lite model in %LOCALAPPDATA%\Lemon\models; skipped when it isn't downloaded.</summary>
public sealed class BackgroundRemovalTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly ConversionService _service = new(new ToolLocator(searchSystem: false));
    private static readonly Preset Lite = BuiltInPresets.All.First(p => p.Id == "img-nobg");

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Plain_image_converter_ignores_remove_background_presets()
    {
        Assert.False(new ImageConverter().Supports("jpg", Lite));
        Assert.Equal(BackgroundModelInfo.Get(BackgroundModel.Lite).IsInstalled, _service.CanConvert("photo.jpg", Lite));
    }

    [Fact]
    public async Task Removes_the_background_and_keeps_the_size()
    {
        Assert.SkipUnless(BackgroundModelInfo.Get(BackgroundModel.Lite).IsInstalled, "BiRefNet Lite isn't downloaded");
        var source = _temp.File("subject.jpg");
        using (var image = new MagickImage(MagickColors.SkyBlue, 500, 400))
        {
            new Drawables().FillColor(MagickColors.OrangeRed).Ellipse(250, 200, 120, 150, 0, 360).Draw(image);
            image.Write(source);
        }

        var result = await _service.ConvertAsync(source, Lite, null);

        Assert.EndsWith(" (no background).png", result);
        using var cut = new MagickImage(result);
        Assert.Equal((500u, 400u), (cut.Width, cut.Height));
        Assert.True(cut.HasAlpha);
        using var pixels = cut.GetPixels();
        var alpha = (Func<int, int, double>)((x, y) => pixels.GetPixel(x, y).ToColor()!.A / 65535.0);
        Assert.True(alpha(5, 5) < 0.2, "the background should be see-through");
    }
}
