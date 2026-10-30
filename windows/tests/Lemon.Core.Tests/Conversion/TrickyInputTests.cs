using ImageMagick;

namespace Lemon.Core.Tests;

/// <summary>File names and picture sizes that have tripped up the tools underneath.</summary>
public sealed class TrickyInputTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly ConversionService _service = new(new ToolLocator());

    public void Dispose() => _temp.Dispose();

    private string Picture(string name, uint width = 64, uint height = 48)
    {
        // Through a stream, so the test's own writing can't trip on the name.
        var path = _temp.File(name);
        using var image = new MagickImage(MagickColors.Orange, width, height);
        using var file = File.Create(path);
        image.Write(file, Path.GetExtension(name) == ".jpg" ? MagickFormat.Jpeg : MagickFormat.Png);
        return path;
    }

    [Theory]
    [InlineData("100%d.png")]
    [InlineData("frame%03d.png")]
    [InlineData("50% off.png")]
    [InlineData("%s.png")]
    [InlineData("a[0].png")]
    [InlineData("b[1-2].png")]
    public async Task NamesImageMagickCouldReadAsPatterns(string name)
    {
        var source = Picture(name);

        var output = await _service.ConvertAsync(source, new Preset { Name = "x", TargetExtension = "jpg" }, null);

        Assert.Equal(Path.GetFileNameWithoutExtension(name) + ".jpg", Path.GetFileName(output));
        Assert.True(new FileInfo(output).Length > 0);
        using var result = new MagickImage(output);
        Assert.Equal(64u, result.Width);
    }

    [Theory]
    [InlineData("100%d.png")]
    [InlineData("frame%03d.png")]
    public async Task ResizeNamesImageMagickCouldReadAsPatterns(string name)
    {
        var source = Picture(name, 800, 600);

        var output = await _service.ConvertAsync(source, new Preset { Name = "x", TargetExtension = Preset.SameFormat, MaxWidth = 100 }, null);

        using var result = new MagickImage(output);
        Assert.Equal(100u, result.Width);
    }

    [Fact]
    public async Task APatternNameLeavesOtherFilesAlone()
    {
        var source = Picture("frame%03d.png");
        var bystander = _temp.File("frame000.jpg");
        File.WriteAllText(bystander, "not a picture");

        await _service.ConvertAsync(source, new Preset { Name = "x", TargetExtension = "jpg" }, null);

        Assert.Equal("not a picture", File.ReadAllText(bystander));
    }

    [Theory]
    [InlineData("gif")]
    [InlineData("webp")]
    [InlineData("pdf")]
    public async Task PatternNamesInEveryWayOfWriting(string target)
    {
        var source = Picture("frame%03d.png");

        var output = await _service.ConvertAsync(source, new Preset { Name = "x", TargetExtension = target }, null);

        Assert.True(new FileInfo(output).Length > 0);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(5, 3)]
    [InlineData(3, 400)]
    public async Task OptimizeHandlesTinyPictures(uint width, uint height)
    {
        var source = Picture("tiny.jpg", width, height);

        var output = await _service.ConvertAsync(source, BuiltInPresets.All.Single(p => p.Id == BuiltInPresets.OptimizeId), null);

        Assert.True(File.Exists(output));
    }
}
