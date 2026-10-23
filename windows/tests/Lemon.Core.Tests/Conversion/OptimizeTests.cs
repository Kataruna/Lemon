using System.Globalization;
using ImageMagick;
using ImageMagick.Drawing;

namespace Lemon.Core.Tests;

/// <summary>Optimize mode on real files. Video tests are skipped when FFmpeg isn't installed, PDF ones without Ghostscript.</summary>
public sealed class OptimizeTests : IDisposable
{
    private static readonly Preset Optimize = BuiltInPresets.All.Single(p => p.Id == BuiltInPresets.OptimizeId);
    private static readonly Preset Lossless = BuiltInPresets.All.Single(p => p.Id == "optimize-lossless");
    private static readonly Preset Smaller = BuiltInPresets.All.Single(p => p.Id == "optimize-smaller");

    private readonly TempFolder _temp = new();
    private readonly ToolLocator _tools = new();
    private readonly ConversionService _service;

    public OptimizeTests() => _service = new ConversionService(_tools);

    public void Dispose() => _temp.Dispose();

    /// <summary>A smooth, photo-like image saved at a wasteful JPEG quality.</summary>
    private string MakePhoto(string name = "photo.jpg", uint quality = 98)
    {
        var path = _temp.File(name);
        using var image = new MagickImage("plasma:fractal", new MagickReadSettings { Width = 1200, Height = 800 });
        image.Blur(0, 3);
        image.Quality = quality;
        image.Write(path, MagickFormat.Jpeg);
        return path;
    }

    /// <summary>Screenshot-like: flat colors with anti-aliased text and shapes, well over 256 colors in all.</summary>
    private string MakeScreenshot(string name = "screen.png")
    {
        var path = _temp.File(name);
        using var image = new MagickImage(MagickColors.White, 1000, 700);
        var drawables = new Drawables().FontPointSize(18);
        for (var i = 0; i < 25; i++)
        {
            drawables.FillColor(new MagickColor((byte)(i * 9), (byte)(80 + i * 5), (byte)(200 - i * 7)))
                .Rectangle(20, 20 + i * 26, 300, 40 + i * 26)
                .Text(320, 36 + i * 26, $"Line {i}: the quick brown fox jumps over the lazy dog");
        }
        drawables.Draw(image);
        image.Write(path, MagickFormat.Png);
        return path;
    }

    private static double Dissimilarity(string a, string b)
    {
        using var first = new MagickImage(a);
        using var second = new MagickImage(b);
        return first.Compare(second, ErrorMetric.StructuralDissimilarity);
    }

    [Fact]
    public async Task ShrinksHighQualityJpegWithoutVisibleChange()
    {
        var source = MakePhoto();

        var output = await _service.ConvertAsync(source, Optimize, null);

        Assert.Equal(_temp.File("photo (optimized).jpg"), output);
        Assert.True(new FileInfo(output).Length < new FileInfo(source).Length * 0.9);
        Assert.True(Dissimilarity(source, output) <= 0.01);
        Assert.True(File.Exists(source));
    }

    [Fact]
    public async Task LosslessKeepsEveryPixel()
    {
        var source = MakePhoto();

        var output = await _service.ConvertAsync(source, Lossless, null);

        Assert.NotEqual(source, output);
        Assert.True(new FileInfo(output).Length < new FileInfo(source).Length);
        using var before = new MagickImage(source);
        using var after = new MagickImage(output);
        Assert.Equal(0, before.Compare(after, ErrorMetric.Absolute));
    }

    [Fact]
    public async Task SmallerBeatsBalancedAndStillLooksClose()
    {
        var source = MakePhoto();

        var balanced = await _service.ConvertAsync(source, Optimize, _temp.File("balanced"));
        var smaller = await _service.ConvertAsync(source, Smaller, _temp.File("smaller"));

        Assert.True(new FileInfo(smaller).Length < new FileInfo(balanced).Length);
        Assert.True(Dissimilarity(source, smaller) <= 0.025);
    }

    [Fact]
    public void LosslessIsOnlyOfferedWhereItCanBeKept()
    {
        Assert.True(_service.CanConvert("photo.png", Lossless));
        Assert.True(_service.CanConvert("photo.jpg", Lossless));
        // WebP has no lossless recompression, and video or PDF re-encodes always change pixels.
        Assert.False(_service.CanConvert("photo.webp", Lossless));
        Assert.False(_service.CanConvert("clip.mp4", Lossless));
        Assert.False(_service.CanConvert("scan.pdf", Lossless));
        Assert.True(_service.CanConvert("photo.webp", Smaller));
    }

    [Fact]
    public void SmallerVideoAndPdfSqueezeHarder()
    {
        var video = VideoOptimizeConverter.BuildArguments(new ConversionRequest("in.mp4", "out.mp4", Smaller));
        Assert.Equal("28", video[video.IndexOf("-crf") + 1]);
        var balancedVideo = VideoOptimizeConverter.BuildArguments(new ConversionRequest("in.mp4", "out.mp4", Optimize));
        Assert.Equal("23", balancedVideo[balancedVideo.IndexOf("-crf") + 1]);

        var pdf = PdfOptimizeConverter.BuildArguments(new ConversionRequest("in.pdf", "out.pdf", Smaller));
        Assert.Contains("-dPDFSETTINGS=/ebook", pdf);
        Assert.Contains("-dColorImageResolution=150", pdf);
    }

    [Fact]
    public async Task KeepsTheOriginalWhenNothingSmallerIsFound()
    {
        var once = await _service.ConvertAsync(MakePhoto(), Optimize, null);

        var twice = await _service.ConvertAsync(once, Optimize, null);

        Assert.Equal(once, twice);
        // No "(optimized) (optimized)" copy or empty placeholder is left behind.
        Assert.Equal(2, Directory.GetFiles(_temp.Path).Length);
    }

    [Theory]
    [InlineData(80u)] // lossless path only
    [InlineData(98u)] // re-encoded at quality 85
    public async Task KeepsJpegOrientationAndColorProfile(uint quality)
    {
        var source = _temp.File("phone.jpg");
        using (var image = new MagickImage("plasma:fractal", new MagickReadSettings { Width = 1200, Height = 770 }))
        {
            image.Blur(0, 3);
            image.SetProfile(ColorProfiles.AdobeRGB1998);
            // Like a phone photo taken in portrait: the pixels are stored sideways and EXIF says how to turn them.
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.Orientation, (ushort)6);
            image.SetProfile(exif);
            image.Orientation = OrientationType.RightTop;
            // Plain Huffman tables, as many cameras write them, leave the lossless path something to gain.
            image.Settings.SetDefine(MagickFormat.Jpeg, "optimize-coding", "false");
            image.Quality = quality;
            image.Write(source, MagickFormat.Jpeg);
        }

        var output = await _service.ConvertAsync(source, Optimize, null);

        Assert.NotEqual(source, output);
        using var before = new MagickImage(source);
        using var after = new MagickImage(output);
        Assert.Equal(OrientationType.RightTop, after.Orientation);
        Assert.Equal(before.GetColorProfile()!.ToByteArray(), after.GetColorProfile()?.ToByteArray());
        if (quality <= 85)
            Assert.Equal(0, before.Compare(after, ErrorMetric.Absolute));
    }

    [Fact]
    public async Task KeepsPngColorProfile()
    {
        var source = MakeScreenshot();
        using (var image = new MagickImage(source))
        {
            image.SetProfile(ColorProfiles.AdobeRGB1998);
            image.Write(source, MagickFormat.Png);
        }

        var output = await _service.ConvertAsync(source, Optimize, null);

        Assert.NotEqual(source, output);
        using var after = new MagickImage(output);
        Assert.Equal(ColorProfiles.AdobeRGB1998.ToByteArray(), after.GetColorProfile()?.ToByteArray());
    }

    [Fact]
    public async Task LeavesSixteenBitPngAlone()
    {
        var source = _temp.File("deep.png");
        using (var image = new MagickImage("gradient:navy-orange", new MagickReadSettings { Width = 300, Height = 300 }))
        {
            image.Settings.SetDefine(MagickFormat.Png, "bit-depth", "16");
            image.Write(source, MagickFormat.Png);
        }
        Assert.True(ImageOptimizeConverter.IsDeepPng(File.ReadAllBytes(source)));

        Assert.Equal(source, await _service.ConvertAsync(source, Optimize, null));
    }

    [Fact]
    public async Task ShrinksScreenshotPng()
    {
        var source = MakeScreenshot();

        var output = await _service.ConvertAsync(source, Optimize, _temp.File("out"));

        Assert.Equal(Path.Combine(_temp.File("out"), "screen (optimized).png"), output);
        Assert.True(new FileInfo(output).Length < new FileInfo(source).Length);
        Assert.True(Dissimilarity(source, output) <= 0.01);
    }

    [Fact]
    public async Task KeepsSoftTransparencyInPng()
    {
        var source = _temp.File("shadow.png");
        using (var image = new MagickImage("radial-gradient:black-none", new MagickReadSettings { Width = 400, Height = 400 }))
        {
            new Drawables().FillColor(MagickColors.OrangeRed).Circle(200, 200, 200, 120).Draw(image);
            image.Write(source, MagickFormat.Png);
        }

        var output = await _service.ConvertAsync(source, Optimize, null);

        using var result = new MagickImage(output);
        using var alpha = result.Separate(Channels.Alpha).First();
        // A soft shadow has many levels of transparency; on/off transparency would leave only two.
        Assert.True(alpha.TotalColors > 16, $"only {alpha.TotalColors} alpha levels left");
        Assert.True(Dissimilarity(source, output) <= 0.01);
    }

    [Fact]
    public async Task KeepsEveryFrameOfAnimatedGif()
    {
        var source = _temp.File("spinner.gif");
        using (var frames = new MagickImageCollection())
        {
            for (var i = 0; i < 6; i++)
            {
                var frame = new MagickImage(MagickColors.White, 120, 120) { AnimationDelay = 10 };
                new Drawables().FillColor(MagickColors.SteelBlue).Circle(60 + 40 * Math.Cos(i), 60 + 40 * Math.Sin(i), 70 + 40 * Math.Cos(i), 60 + 40 * Math.Sin(i)).Draw(frame);
                frames.Add(frame);
            }
            frames.Write(source, MagickFormat.Gif);
        }

        var output = await _service.ConvertAsync(source, Optimize, null);

        using var result = new MagickImageCollection(output);
        Assert.Equal(6, result.Count);
    }

    [Fact]
    public async Task LeavesAnimatedWebPAlone()
    {
        var source = _temp.File("anim.webp");
        using (var frames = new MagickImageCollection())
        {
            frames.Add(new MagickImage(MagickColors.Red, 64, 64) { AnimationDelay = 10 });
            frames.Add(new MagickImage(MagickColors.Blue, 64, 64) { AnimationDelay = 10 });
            frames.Write(source, MagickFormat.WebP);
        }

        Assert.Equal(source, await _service.ConvertAsync(source, Optimize, null));
        Assert.Single(Directory.GetFiles(_temp.Path));
    }

    [Fact]
    public void DetectsAnimatedPng()
    {
        Assert.True(ImageOptimizeConverter.IsAnimatedPng("\x89PNG....IHDR....acTL....IDAT"u8));
        Assert.False(ImageOptimizeConverter.IsAnimatedPng("\x89PNG....IHDR....IDAT....acTL"u8));
        Assert.False(ImageOptimizeConverter.IsAnimatedPng("\x89PNG....IHDR....IDAT"u8));
    }

    [Fact]
    public void RoutesOptimizeByKind()
    {
        var service = new ConversionService(new ToolLocator(searchSystem: false));
        Assert.IsType<ImageOptimizeConverter>(service.FindConverter("jpg", Preset.SameFormat));
        Assert.IsType<ImageOptimizeConverter>(service.FindConverter("png", Preset.SameFormat));
        Assert.Null(service.FindConverter("bmp", Preset.SameFormat));
        Assert.Null(service.FindConverter("mp4", Preset.SameFormat));
        Assert.Null(service.FindConverter("mp3", Preset.SameFormat));
        Assert.Equal([ExternalTool.Ghostscript], service.MissingToolsFor("scan.pdf"));
        Assert.Contains(ExternalTool.Ffmpeg, service.MissingToolsFor("clip.mov"));
    }

    // ---- Video ----

    [Theory]
    [InlineData("h264", "yuv420p", "bt709", true)]
    [InlineData("mpeg4", "yuv420p", null, true)]
    [InlineData("hevc", "yuv420p", null, false)]
    [InlineData("av1", "yuv420p", null, false)]
    [InlineData("h264", "yuv420p10le", null, false)]
    [InlineData("prores", "yuv422p10le", null, false)]
    [InlineData("h264", "yuv420p", "smpte2084", false)]
    [InlineData(null, null, null, false)]
    public void ReencodesOnlyWhereH264CanWinWithoutLosingColor(string? codec, string? pixelFormat, string? transfer, bool expected) =>
        Assert.Equal(expected, VideoOptimizeConverter.IsWorthReencoding(new VideoOptimizeConverter.VideoInfo(codec, pixelFormat, transfer, 10)));

    [Fact]
    public void VideoArgumentsCopyAudioAndSubtitles()
    {
        var mkv = VideoOptimizeConverter.BuildArguments(new ConversionRequest("in.mkv", "out.mkv", Optimize));
        Assert.Equal("copy", mkv[mkv.IndexOf("-c:a") + 1]);
        Assert.Equal("copy", mkv[mkv.IndexOf("-c:s") + 1]);
        Assert.Contains("0:t?", mkv);
        Assert.DoesNotContain("-movflags", mkv);

        var mp4 = VideoOptimizeConverter.BuildArguments(new ConversionRequest("in.mp4", "out.mp4", Optimize));
        Assert.Contains("0:s?", mp4);
        Assert.DoesNotContain("0:t?", mp4);
        Assert.Contains("+faststart", mp4);
    }

    private string RequireFfmpeg()
    {
        var ffmpeg = _tools.Find(ExternalTool.Ffmpeg);
        Assert.SkipWhen(ffmpeg is null, "FFmpeg isn't installed.");
        return ffmpeg!;
    }

    private async Task<string> MakeVideoAsync(string name, params string[] codecArgs)
    {
        var path = _temp.File(name);
        var result = await ProcessRunner.RunAsync(RequireFfmpeg(),
        [
            "-hide_banner", "-y",
            "-f", "lavfi", "-i", "testsrc2=size=1280x720:rate=30:duration=2",
            "-f", "lavfi", "-i", "sine=frequency=440:duration=2",
            .. codecArgs, "-c:a", "aac", "-shortest", path,
        ]);
        Assert.True(result.ExitCode == 0, result.StandardError);
        return path;
    }

    private async Task<string> ProbeAsync(string path, string entries = "stream=codec_name")
    {
        var lines = new List<string>();
        var result = await ProcessRunner.RunAsync(_tools.Find(ExternalTool.Ffprobe)!,
            ["-v", "error", "-show_entries", entries, "-of", "default=noprint_wrappers=1", path], lines.Add);
        Assert.Equal(0, result.ExitCode);
        return string.Join("\n", lines);
    }

    [Fact]
    public async Task ShrinksBloatedVideoAndCopiesAudio()
    {
        // Near-lossless H.264 is several times larger than it needs to be.
        var source = await MakeVideoAsync("big.mp4", "-c:v", "libx264", "-crf", "4", "-preset", "ultrafast", "-pix_fmt", "yuv420p");
        var reports = new List<double>();

        var output = await _service.ConvertAsync(source, Optimize, null, new SyncProgress(reports.Add));

        Assert.Equal(_temp.File("big (optimized).mp4"), output);
        Assert.True(new FileInfo(output).Length < new FileInfo(source).Length / 2);
        var streams = await ProbeAsync(output);
        Assert.Contains("codec_name=h264", streams);
        Assert.Contains("codec_name=aac", streams);
        Assert.Equal(1.0, reports[^1]);
    }

    [Fact]
    public async Task HandlesOddSizedVideo()
    {
        // MJPEG reports yuvj420p, so it qualifies, and unlike H.264 it allows odd sizes.
        var source = await MakeVideoAsync("odd.mkv", "-s", "1279x719", "-c:v", "mjpeg", "-q:v", "2", "-pix_fmt", "yuvj420p");
        Assert.Contains("width=1279", await ProbeAsync(source, "stream=width"));

        var output = await _service.ConvertAsync(source, Optimize, null);

        var info = await ProbeAsync(output, "stream=codec_name,width,height");
        Assert.Contains("codec_name=h264", info);
        Assert.Contains("width=1278", info);
        Assert.Contains("height=718", info);
    }

    [Fact]
    public async Task LeavesHevcVideoAlone()
    {
        var source = await MakeVideoAsync("hevc.mp4", "-c:v", "libx265", "-preset", "ultrafast", "-pix_fmt", "yuv420p");

        Assert.Equal(source, await _service.ConvertAsync(source, Optimize, null));
        Assert.Single(Directory.GetFiles(_temp.Path));
    }

    // ---- PDF ----

    [Fact]
    public void PdfArgumentsEscapePercentInOutputName()
    {
        var args = PdfOptimizeConverter.BuildArguments(new ConversionRequest(@"C:\in 100%.pdf", @"C:\in 100% (optimized).pdf", Optimize));
        Assert.Contains(@"-sOutputFile=C:\in 100%% (optimized).pdf", args);
        Assert.Equal(@"C:\in 100%.pdf", args[^1]);
    }

    [Fact]
    public async Task ShrinksPdfWithOversizedImages()
    {
        Assert.SkipWhen(_tools.Find(ExternalTool.Ghostscript) is null, "Ghostscript isn't installed.");
        var source = _temp.File("scan.pdf");
        using (var page = new MagickImage("plasma:fractal", new MagickReadSettings { Width = 2480, Height = 3508 }))
        {
            // An A4 page scanned at 300 dpi and stored losslessly.
            page.Density = new Density(300, 300, DensityUnit.PixelsPerInch);
            page.Settings.Compression = CompressionMethod.Zip;
            page.Write(source, MagickFormat.Pdf);
        }

        var output = await _service.ConvertAsync(source, Optimize, null);

        Assert.Equal(_temp.File("scan (optimized).pdf"), output);
        Assert.True(new FileInfo(output).Length < new FileInfo(source).Length);
    }
}
