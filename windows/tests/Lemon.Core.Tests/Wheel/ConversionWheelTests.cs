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
    public void RemoveBackgroundModelsGetTheirOwnRing()
    {
        IReadOnlyList<Preset> available = [Built("img-nobg-full"), Built("img-png"), Built("img-nobg"), Built("optimize-smaller")];

        Assert.Equal(["img-png"], ConversionWheel.Choose([available], MediaKind.Image).Select(p => p.Id));
        Assert.Equal(["img-nobg", "img-nobg-full"],
            ConversionWheel.Choose([available], MediaKind.Image, WheelPage.RemoveBackground).Select(p => p.Id));
    }

    [Fact]
    public void ResizeSizesGetTheirOwnRingLargestFirst()
    {
        IReadOnlyList<Preset> available = [Built("resize-sd"), Built("img-png"), Built("resize-fhd"), Built("optimize-smaller"), Built("resize-4k"), Built("resize-hd")];

        Assert.Equal(["img-png"], ConversionWheel.Choose([available], MediaKind.Image).Select(p => p.Id));
        Assert.Equal(["optimize-smaller"], ConversionWheel.Choose([available], MediaKind.Image, WheelPage.Optimize).Select(p => p.Id));
        Assert.Equal(["resize-4k", "resize-fhd", "resize-hd", "resize-sd"],
            ConversionWheel.Choose([available], MediaKind.Image, WheelPage.Resize).Select(p => p.Id));
        Assert.Equal(("Full HD", "1920 px"), ConversionWheel.Label(Built("resize-fhd")));
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

    /// <summary>What a single PNG can use without AI models: image formats, the Optimize strengths and the sizes.</summary>
    private static IReadOnlyList<(MediaKind, IReadOnlyList<Preset>)> Png() =>
        [(MediaKind.Image, BuiltInPresets.All
            .Where(p => p.RemoveBackground is null && p.Upscale is null && (p.Id.StartsWith("img-") || p.IsOptimize || p.IsResize || p.Id == "doc-pdf"))
            .ToList())];

    private static string[] Ids(WheelPlan plan) => [.. plan.Rings.Select(r => r.Id)];

    [Fact]
    public void HandPickedConvertRingShowsOnlyThoseInThatOrder()
    {
        var settings = new WheelSettings { ConvertRing = { [MediaKind.Image] = ["img-webp", "img-avif", "aud-mp3-320"] } };

        var plan = ConversionWheel.Plan(Png(), MediaKind.Image, settings);

        // MP3 can't be made from a PNG, so it's skipped.
        Assert.Equal(["img-webp", "img-avif"], plan.Choices[0].Select(p => p.Id));
    }

    [Fact]
    public void CustomRingsMixAnyPresetsAndHiddenRingsAreLeftOut()
    {
        var mine = new CustomRing { Name = "Web", PresetIds = ["resize-fhd", "optimize-smaller", "img-webp", "vid-mp4"] };
        var settings = new WheelSettings { Rings = [mine], HiddenRings = ["optimize", "cutout"] };

        var plan = ConversionWheel.Plan(Png(), MediaKind.Image, settings);

        Assert.Equal(["convert", "resize", mine.Id], Ids(plan));
        Assert.Equal(["resize-fhd", "optimize-smaller", "img-webp"], plan.Choices[2].Select(p => p.Id));
        Assert.False(plan.HubOn);
    }

    [Fact]
    public void OpensOnTheRingChosenForTheKindOrTheHub()
    {
        var settings = new WheelSettings { OpensOn = { [MediaKind.Image] = "resize" } };
        var plan = ConversionWheel.Plan(Png(), MediaKind.Image, settings);
        Assert.Equal("resize", plan.Rings[plan.Start].Id);
        Assert.False(plan.OpensOnHub);

        settings.OpenOnHub = true;
        plan = ConversionWheel.Plan(Png(), MediaKind.Image, settings);
        Assert.True(plan.HubOn);
        Assert.True(plan.OpensOnHub);
    }

    [Fact]
    public void AChosenRingWithNothingForTheFilesFallsBackToTheFirst()
    {
        // Cut out has nothing without a downloaded model; Png() doesn't include it.
        var settings = new WheelSettings { OpensOn = { [MediaKind.Image] = "cutout" } };
        var plan = ConversionWheel.Plan(Png(), MediaKind.Image, settings);
        Assert.Equal(0, plan.Start);
        Assert.False(plan.OpensOnHub);
    }

    [Fact]
    public void MoreRingsThanFitOnTheBarTurnTheHubOn()
    {
        var settings = new WheelSettings { Rings = [new() { Name = "A", PresetIds = ["img-png"] }, new() { Name = "B", PresetIds = ["img-jpg"] }] };

        var plan = ConversionWheel.Plan(Png(), MediaKind.Image, settings);

        Assert.Equal(5, plan.Rings.Count);
        Assert.True(plan.HubOn);
        // Nothing chosen: still opens on the first ring; the hub is one step away.
        Assert.False(plan.OpensOnHub);
    }

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
