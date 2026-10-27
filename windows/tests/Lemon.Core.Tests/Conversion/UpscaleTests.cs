using Lemon.Core.Models;
using ImageMagick;
using ImageMagick.Drawing;

namespace Lemon.Core.Tests;

/// <summary>Needs the Real-ESRGAN General model in %LOCALAPPDATA%\Lemon\models; skipped when it isn't downloaded.</summary>
public sealed class UpscaleTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly ConversionService _service = new(new ToolLocator(searchSystem: false));
    private static readonly Preset Double = BuiltInPresets.All.First(p => p.Id == "upscale-2x");

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Only_the_upscaler_takes_upscale_presets_once_its_model_is_there()
    {
        Assert.True(Double.IsResize);
        Assert.False(Double.IsOptimize);
        Assert.False(new ImageConverter().Supports("jpg", Double));
        Assert.Equal(UpscaleModelInfo.Get(UpscaleModel.Lite).IsInstalled, _service.CanConvert("photo.jpg", Double));
        Assert.False(_service.CanConvert("anim.gif", Double));
    }

    [Theory]
    [InlineData("upscale-2x", 2)]
    [InlineData("upscale-4x", 4)]
    public async Task Enlarges_and_keeps_transparency(string id, int scale)
    {
        Assert.SkipUnless(UpscaleModelInfo.Get(UpscaleModel.Lite).IsInstalled, "Real-ESRGAN General isn't downloaded");
        // Wider and taller than one tile, so the seams between tiles are crossed.
        var source = _temp.File("logo.png");
        using (var image = new MagickImage(MagickColors.Transparent, 300, 280))
        {
            new Drawables().FillColor(MagickColors.OrangeRed).Ellipse(150, 140, 100, 90, 0, 360).Draw(image);
            image.Write(source);
        }

        var result = await _service.ConvertAsync(source, BuiltInPresets.All.First(p => p.Id == id), null);

        Assert.EndsWith($" ({scale}×).png", result);
        using var big = new MagickImage(result);
        Assert.Equal((300u * (uint)scale, 280u * (uint)scale), (big.Width, big.Height));
        using var pixels = big.GetPixels();
        var color = pixels.GetPixel(150 * scale, 140 * scale).ToColor()!;
        Assert.True(color.R > color.B * 2, "the middle should stay orange-red");
        Assert.True(pixels.GetPixel(2, 2).ToColor()!.A < 25, "the corner should stay see-through");
    }

    [Fact]
    public async Task Keeps_cmyk_colors()
    {
        Assert.SkipUnless(UpscaleModelInfo.Get(UpscaleModel.Lite).IsInstalled, "Real-ESRGAN General isn't downloaded");
        var source = _temp.File("print.jpg");
        using (var image = new MagickImage(MagickColors.OrangeRed, 120, 80))
        {
            image.ColorSpace = ColorSpace.CMYK;
            image.Write(source);
        }

        var result = await _service.ConvertAsync(source, Double, null);

        using var big = new MagickImage(result);
        Assert.Equal(ColorSpace.CMYK, big.ColorSpace);
        big.ColorSpace = ColorSpace.sRGB;
        using var pixels = big.GetPixels();
        var color = pixels.GetPixel(120, 80).ToColor()!;
        Assert.True(color.R > 200 && color.G < 120 && color.B < 60, $"orange-red came out as {color}");
    }

    [Fact]
    public async Task Puts_every_tile_back_in_its_place()
    {
        Assert.SkipUnless(UpscaleModelInfo.Get(UpscaleModel.Lite).IsInstalled, "Real-ESRGAN General isn't downloaded");
        // One color per quadrant; each quadrant's middle lands in a different 256 px tile.
        var source = _temp.File("quadrants.png");
        IMagickColor<byte>[] colors = [MagickColors.Red, MagickColors.Lime, MagickColors.Blue, MagickColors.Yellow];
        using (var image = new MagickImage(MagickColors.Black, 600, 560))
        {
            for (var i = 0; i < 4; i++)
                new Drawables().FillColor(colors[i]).Rectangle(i % 2 * 300, i / 2 * 280, i % 2 * 300 + 299, i / 2 * 280 + 279).Draw(image);
            image.Write(source);
        }

        var result = await _service.ConvertAsync(source, BuiltInPresets.All.First(p => p.Id == "upscale-2x"), null);

        using var big = new MagickImage(result);
        using var pixels = big.GetPixels();
        for (var i = 0; i < 4; i++)
        {
            var got = pixels.GetPixel((i % 2 * 300 + 150) * 2, (i / 2 * 280 + 140) * 2).ToColor()!;
            Assert.True(Math.Abs(got.R - colors[i].R) + Math.Abs(got.G - colors[i].G) + Math.Abs(got.B - colors[i].B) < 30, $"quadrant {i} came out as {got}");
        }
    }
}
