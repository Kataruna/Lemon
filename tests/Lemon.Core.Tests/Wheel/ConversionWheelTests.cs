namespace Lemon.Core.Tests;

public class ConversionWheelTests
{
    private static Preset Built(string id) => BuiltInPresets.All.Single(p => p.Id == id);

    [Fact]
    public void FavoritesComeFirstAndUserPresetsBeforeThem()
    {
        var mine = new Preset { Name = "Tiny JPEG", TargetExtension = "jpg", Quality = 40 };
        var available = BuiltInPresets.All.Where(p => p.TargetKind == MediaKind.Image).Append(mine).ToList();

        var choices = ConversionWheel.Choose([available], MediaKind.Image);

        Assert.Equal(ConversionWheel.MaxChoices, choices.Count);
        Assert.Same(mine, choices[0]);
        Assert.Equal(["img-png", "img-jpg", "img-webp"], choices.Skip(1).Take(3).Select(p => p.Id));
    }

    [Fact]
    public void OnlyOffersPresetsThatWorkForEveryFile()
    {
        IReadOnlyList<Preset> forMp4 = [Built("vid-mp4"), Built("vid-gif"), Built("aud-mp3-320")];
        IReadOnlyList<Preset> forWav = [Built("aud-mp3-320"), Built("aud-flac")];
        Assert.Equal(["aud-mp3-320"], ConversionWheel.Choose([forMp4, forWav], MediaKind.Video).Select(p => p.Id));
    }

    [Fact]
    public void VideoWheelPutsVideoFormatsBeforeAudioExtraction()
    {
        IReadOnlyList<Preset> available = [Built("aud-mp3-320"), Built("vid-webm"), Built("vid-gif"), Built("vid-mp4")];
        Assert.Equal(["vid-mp4", "vid-webm", "vid-gif", "aud-mp3-320"], ConversionWheel.Choose([available], MediaKind.Video).Select(p => p.Id));
    }

    [Fact]
    public void NothingDraggedMeansNoChoices() => Assert.Empty(ConversionWheel.Choose([], MediaKind.Image));

    [Theory]
    [InlineData("MP4 – 720p small", "mp4", "MP4", "720p small")]
    [InlineData("JPEG – High quality", "jpg", "JPG", "High quality")]
    [InlineData("Animated GIF", "gif", "GIF", "Animated GIF")]
    [InlineData("Windows icon (256 px)", "ico", "ICO", "Windows icon (256 px)")]
    [InlineData("PNG", "png", "PNG", null)]
    [InlineData("Word (DOCX)", "docx", "DOCX", null)]
    [InlineData("WebM (VP9)", "webm", "WEBM", "VP9")]
    [InlineData("QuickTime MOV", "mov", "MOV", null)]
    [InlineData("MP4 (H.264)", "mp4", "MP4", "H.264")]
    public void LabelsShowShortFormatAndDetail(string name, string ext, string title, string? detail) =>
        Assert.Equal((title, detail), ConversionWheel.Label(new Preset { Name = name, TargetExtension = ext }));
}
