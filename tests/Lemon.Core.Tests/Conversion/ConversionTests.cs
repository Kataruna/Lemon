using System.Globalization;
using System.Text.RegularExpressions;
using ImageMagick;
using ImageMagick.Drawing;

namespace Lemon.Core.Tests;

/// <summary>End-to-end conversions of real files. FFmpeg tests are skipped when FFmpeg isn't installed.</summary>
public sealed class ConversionTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly ToolLocator _tools = new();
    private readonly ConversionService _service;

    public ConversionTests() => _service = new ConversionService(_tools);

    public void Dispose() => _temp.Dispose();

    private static Preset P(string ext, Func<Preset, Preset>? configure = null)
    {
        var preset = new Preset { Name = ext, TargetExtension = ext };
        return configure?.Invoke(preset) ?? preset;
    }

    private string MakePng(int width = 640, int height = 480)
    {
        var path = _temp.File("source.png");
        using var image = new MagickImage(MagickColors.Transparent, (uint)width, (uint)height);
        new Drawables().FillColor(MagickColors.Orange).Ellipse(width / 2.0, height / 2.0, width / 3.0, height / 3.0, 0, 360).Draw(image);
        image.Write(path, MagickFormat.Png);
        return path;
    }

    [Theory]
    [InlineData("jpg")]
    [InlineData("webp")]
    [InlineData("avif")]
    [InlineData("gif")]
    [InlineData("bmp")]
    [InlineData("tiff")]
    [InlineData("ico")]
    public async Task ConvertsImages(string target)
    {
        var source = MakePng();
        Assert.SkipUnless(_service.FindConverter("png", target) is not null, $"This Magick.NET build can't write {target}.");

        var output = await _service.ConvertAsync(source, P(target, p => p with { Quality = 70 }), null);

        Assert.Equal("." + target, Path.GetExtension(output));
        using var result = new MagickImage(output);
        if (target == "ico")
            Assert.True(result.Width <= 256 && result.Height <= 256);
        else
            Assert.Equal(640u, result.Width);
    }

    private static string PdfText(string path) => System.Text.Encoding.Latin1.GetString(File.ReadAllBytes(path));

    private static int PdfPageCount(string text) => Regex.Matches(text, @"/Type\s*/Page\b(?!s)").Count;

    [Theory]
    [InlineData("jpg", "DCTDecode")]   // photos stay JPEG inside the PDF
    [InlineData("png", "FlateDecode")] // graphics stay lossless
    public async Task ConvertsImageToPdf(string sourceFormat, string expectedFilter)
    {
        var source = _temp.File("photo." + sourceFormat);
        using (var image = new MagickImage("plasma:fractal", new MagickReadSettings { Width = 4032, Height = 3024 }))
            image.Write(source);

        var output = await _service.ConvertAsync(source, BuiltInPresets.All.Single(p => p.Id == "doc-pdf"), null);

        var text = PdfText(output);
        Assert.StartsWith("%PDF-", text);
        Assert.Equal(1, PdfPageCount(text));
        Assert.Contains("/" + expectedFilter, text);
        // No density in the file: the page is scaled to fit A4 (842 pt tall) instead of a 56-inch page at 72 dpi.
        var box = Regex.Match(text, @"/MediaBox\s*\[\s*0\s+0\s+([\d.]+)\s+([\d.]+)").Groups;
        Assert.InRange(double.Parse(box[1].Value, CultureInfo.InvariantCulture), 841, 843);
        Assert.InRange(double.Parse(box[2].Value, CultureInfo.InvariantCulture), 630, 633);
    }

    [Fact]
    public async Task MultiPageTiffBecomesMultiPagePdf()
    {
        var source = _temp.File("scan.tiff");
        using (var pages = new MagickImageCollection())
        {
            for (var i = 0; i < 3; i++)
                pages.Add(new MagickImage(MagickColors.White, 850, 1100) { Density = new Density(100, 100, DensityUnit.PixelsPerInch) });
            pages.Write(source, MagickFormat.Tiff);
        }

        var output = await _service.ConvertAsync(source, BuiltInPresets.All.Single(p => p.Id == "doc-pdf"), null);

        var text = PdfText(output);
        Assert.Equal(3, PdfPageCount(text));
        // 100 dpi fits on A4, so the page keeps its real size: 8.5 x 11 inches.
        Assert.Matches(@"/MediaBox\s*\[\s*0\s+0\s+612\s+792\s*\]", text);
    }

    [Fact]
    public async Task ImageRoundTripThroughAvif()
    {
        var source = MakePng();
        var encoded = await _service.ConvertAsync(source, P("avif"), null);
        var decoded = await _service.ConvertAsync(encoded, P("png"), _temp.File("back"));
        using var image = new MagickImage(decoded);
        Assert.Equal((640u, 480u), (image.Width, image.Height));
    }

    [Fact]
    public void ReadsHeicPhotos()
    {
        // Magick.NET decodes HEIC (iPhone photos) but ships without an HEVC encoder, so HEIC is input-only.
        Assert.IsType<ImageConverter>(_service.FindConverter("heic", "jpg"));
        Assert.Null(_service.FindConverter("png", "heic"));
    }

    [Fact]
    public async Task ResizesWithinBoundsAndKeepsAspect()
    {
        var source = MakePng(4000, 2000);
        var output = await _service.ConvertAsync(source, P("jpg", p => p with { MaxWidth = 1000, MaxHeight = 1000 }), null);
        using var image = new MagickImage(output);
        Assert.Equal((1000u, 500u), (image.Width, image.Height));
    }

    [Fact]
    public async Task MarkdownToHtml()
    {
        var source = _temp.File("notes.md");
        File.WriteAllText(source, "# Hello\n\n| a | b |\n|---|---|\n| 1 | 2 |\n");
        var output = await _service.ConvertAsync(source, P("html"), null);
        var html = File.ReadAllText(output);
        Assert.Contains("<h1 id=\"hello\">Hello</h1>", html);
        Assert.Contains("<table>", html);
        Assert.Contains("<title>notes</title>", html);
    }

    [Fact]
    public async Task UnsupportedPairFailsWithoutLeavingFiles()
    {
        var source = _temp.File("notes.md");
        File.WriteAllText(source, "x");
        await Assert.ThrowsAsync<ConversionException>(() => _service.ConvertAsync(source, P("mp3"), null));
        Assert.Single(Directory.GetFiles(_temp.Path));
    }

    // ---- FFmpeg ----

    private string RequireFfmpeg()
    {
        var ffmpeg = _tools.Find(ExternalTool.Ffmpeg);
        Assert.SkipWhen(ffmpeg is null, "FFmpeg isn't installed.");
        return ffmpeg!;
    }

    private async Task<string> MakeMediaAsync(string name, params string[] lavfiArgs)
    {
        var path = _temp.File(name);
        var result = await ProcessRunner.RunAsync(RequireFfmpeg(), ["-hide_banner", "-y", .. lavfiArgs, path]);
        Assert.True(result.ExitCode == 0, result.StandardError);
        return path;
    }

    private Task<string> MakeVideoAsync(double seconds = 3) => MakeMediaAsync("clip.mp4",
        "-f", "lavfi", "-i", $"testsrc2=size=1280x720:rate=30:duration={seconds.ToString(CultureInfo.InvariantCulture)}",
        "-f", "lavfi", "-i", $"sine=frequency=440:duration={seconds.ToString(CultureInfo.InvariantCulture)}",
        "-c:v", "libx264", "-preset", "ultrafast", "-pix_fmt", "yuv420p", "-c:a", "aac", "-shortest");

    private async Task<string> ProbeAsync(string path, string entries)
    {
        var lines = new List<string>();
        var result = await ProcessRunner.RunAsync(_tools.Find(ExternalTool.Ffprobe)!,
            ["-v", "error", "-show_entries", entries, "-of", "default=noprint_wrappers=1", path], lines.Add);
        Assert.Equal(0, result.ExitCode);
        return string.Join("\n", lines);
    }

    [Theory]
    [InlineData("mp3", "codec_name=mp3")]
    [InlineData("m4a", "codec_name=aac")]
    [InlineData("flac", "codec_name=flac")]
    [InlineData("opus", "codec_name=opus")]
    [InlineData("ogg", "codec_name=vorbis")]
    [InlineData("wav", "codec_name=pcm_s16le")]
    public async Task ConvertsAudio(string target, string expectedCodec)
    {
        var source = await MakeMediaAsync("tone.wav", "-f", "lavfi", "-i", "sine=frequency=440:duration=2");
        var output = await _service.ConvertAsync(source, P(target, p => p with { AudioBitrateKbps = 128 }), null);
        Assert.Contains(expectedCodec, await ProbeAsync(output, "stream=codec_name"));
    }

    [Fact]
    public async Task ConvertsVideoWithProgressAndScaling()
    {
        var source = await MakeVideoAsync();
        var reports = new List<double>();
        var preset = BuiltInPresets.All.Single(p => p.Id == "vid-mp4-720") with { MaxHeight = 480 };

        var output = await _service.ConvertAsync(source, preset, _temp.File("out"), new SyncProgress(reports.Add));

        var info = await ProbeAsync(output, "stream=codec_name,width,height");
        Assert.Contains("codec_name=h264", info);
        Assert.Contains("height=480", info);
        Assert.Contains("width=854", info);
        Assert.Equal(1.0, reports[^1]);
        Assert.True(reports.Count >= 2, "expected intermediate progress reports");
    }

    [Theory]
    [InlineData("webm", "codec_name=vp9")]
    [InlineData("gif", "codec_name=gif")]
    [InlineData("mp3", "codec_name=mp3")]
    public async Task ConvertsVideoToOtherFormats(string target, string expectedCodec)
    {
        var source = await MakeVideoAsync(1);
        var preset = BuiltInPresets.All.First(p => p.TargetExtension == target && p.Id.StartsWith(target == "mp3" ? "aud" : "vid"));
        var output = await _service.ConvertAsync(source, preset, null);
        Assert.Contains(expectedCodec, await ProbeAsync(output, "stream=codec_name"));
    }

    [Fact]
    public async Task CancellingKillsFfmpegAndRemovesPartialOutput()
    {
        var source = await MakeVideoAsync(20);
        using var cts = new CancellationTokenSource();
        var progress = new SyncProgress(p => { if (p > 0.05) cts.Cancel(); });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _service.ConvertAsync(source, P("webm", p => p with { VideoCrf = 30 }), _temp.File("out"), progress, cts.Token));

        Assert.Empty(Directory.GetFiles(_temp.File("out")));
    }

    [Fact]
    public async Task FailedFfmpegRunReportsErrorAndCleansUp()
    {
        RequireFfmpeg();
        var source = _temp.File("broken.mp4");
        File.WriteAllText(source, "this is not a video");
        var error = await Assert.ThrowsAsync<ConversionException>(() => _service.ConvertAsync(source, P("mp3"), _temp.File("out")));
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
        Assert.Empty(Directory.GetFiles(_temp.File("out")));
    }
}
