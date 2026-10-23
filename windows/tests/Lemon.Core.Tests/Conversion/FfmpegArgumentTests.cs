namespace Lemon.Core.Tests;

public class FfmpegArgumentTests
{
    private static List<string> Args(string input, Preset preset) =>
        FfmpegConverter.BuildArguments(new ConversionRequest(input, "out." + preset.TargetExtension, preset));

    [Fact]
    public void AudioUsesBitrateAndDropsVideo()
    {
        var args = Args("a b.wav", new Preset { Name = "x", TargetExtension = "mp3", AudioBitrateKbps = 320 });
        Assert.Equal("a b.wav", args[args.IndexOf("-i") + 1]);
        Assert.Contains("-vn", args);
        Assert.Equal("libmp3lame", args[args.IndexOf("-c:a") + 1]);
        Assert.Equal("320k", args[args.IndexOf("-b:a") + 1]);
        Assert.Equal("out.mp3", args[^1]);
    }

    [Fact]
    public void VideoScalesToEvenSizeWithinLimits()
    {
        var args = Args("in.mov", BuiltInPresets.All.Single(p => p.Id == "vid-mp4-720"));
        var filter = args[args.IndexOf("-vf") + 1];
        Assert.Contains("h='min(ih,720)'", filter);
        Assert.Contains("force_divisible_by=2", filter);
        Assert.Equal("27", args[args.IndexOf("-crf") + 1]);
        Assert.Contains("+faststart", args);
    }

    [Fact]
    public void GifUsesPalette()
    {
        var args = Args("in.mp4", BuiltInPresets.All.Single(p => p.Id == "vid-gif"));
        var filter = args[args.IndexOf("-vf") + 1];
        Assert.StartsWith("fps=12,scale=w='min(iw,480)'", filter);
        Assert.Contains("palettegen", filter);
    }

    [Theory]
    [InlineData("out_time_us=1500000", true, 1.5)]
    [InlineData("out_time_us=N/A", false, 0)]
    [InlineData("frame=12", false, 0)]
    public void ParsesProgress(string line, bool ok, double seconds)
    {
        Assert.Equal(ok, FfmpegConverter.TryParseProgress(line, out var s));
        Assert.Equal(seconds, s);
    }
}
