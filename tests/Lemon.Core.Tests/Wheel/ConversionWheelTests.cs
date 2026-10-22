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
    public void OptimizeStrengthsGetTheirOwnRing()
    {
        IReadOnlyList<Preset> available =
            [Built("optimize-smaller"), Built("img-png"), Built(BuiltInPresets.OptimizeId), Built("img-webp"), Built("optimize-lossless")];

        Assert.Equal(["img-png", "img-webp"], ConversionWheel.Choose([available], MediaKind.Image).Select(p => p.Id));
        Assert.Equal(["optimize-lossless", BuiltInPresets.OptimizeId, "optimize-smaller"],
            ConversionWheel.Choose([available], MediaKind.Image, WheelPage.Optimize).Select(p => p.Id));
    }

    [Fact]
    public void OptimizeRingOnlyOffersStrengthsEveryFileSupports()
    {
        // A video can't be optimized losslessly, so a photo + video drag only gets Balanced and Smaller.
        IReadOnlyList<Preset> forJpg = [Built("optimize-lossless"), Built(BuiltInPresets.OptimizeId), Built("optimize-smaller")];
        IReadOnlyList<Preset> forMp4 = [Built(BuiltInPresets.OptimizeId), Built("optimize-smaller")];
        Assert.Equal([BuiltInPresets.OptimizeId, "optimize-smaller"],
            ConversionWheel.ChooseByKind([(MediaKind.Video, forMp4), (MediaKind.Image, forJpg)], MediaKind.Video, WheelPage.Optimize)
                .Select(p => p.Id));
    }

    [Theory]
    [InlineData("optimize-lossless", "Lossless", "Same pixels")]
    [InlineData(BuiltInPresets.OptimizeId, "Balanced", "Looks the same")]
    [InlineData("optimize-smaller", "Smaller", "Slightly lower quality")]
    public void OptimizeLabelsNameTheStrength(string id, string title, string detail) =>
        Assert.Equal((title, detail), ConversionWheel.Label(Built(id)));

    [Fact]
    public void MixedDragFillsTheWheelForTheMainKind()
    {
        // Photos and a video share just one preset: it comes first, and the free slots go to the photos (the main kind).
        IReadOnlyList<Preset> forJpg = [Built("img-png"), Built("img-webp"), Built("aud-mp3-320")];
        IReadOnlyList<Preset> forMp4 = [Built("aud-mp3-320"), Built("vid-webm")];

        var choices = ConversionWheel.ChooseByKind([(MediaKind.Image, forJpg), (MediaKind.Video, forMp4)], MediaKind.Image);

        Assert.Equal(["aud-mp3-320", "img-png", "img-webp"], choices.Select(p => p.Id));
    }

    [Fact]
    public void DragOfOneKindStillOnlyOffersSharedPresets()
    {
        IReadOnlyList<Preset> forMp4 = [Built("vid-mp4"), Built("vid-gif"), Built("aud-mp3-320")];
        IReadOnlyList<Preset> forMov = [Built("vid-mp4"), Built("aud-mp3-320")];
        Assert.Equal(["vid-mp4", "aud-mp3-320"],
            ConversionWheel.ChooseByKind([(MediaKind.Video, forMp4), (MediaKind.Video, forMov)], MediaKind.Video).Select(p => p.Id));
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
