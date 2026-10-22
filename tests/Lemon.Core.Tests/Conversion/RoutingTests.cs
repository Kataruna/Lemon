namespace Lemon.Core.Tests;

public class RoutingTests
{
    private static ConversionService WithoutTools() => new(new ToolLocator(searchSystem: false));

    [Fact]
    public void BuiltInsWorkWithoutAnyTools()
    {
        var service = WithoutTools();
        Assert.IsType<MarkdownConverter>(service.FindConverter("md", "html"));
        Assert.IsType<ImageConverter>(service.FindConverter("png", "webp"));
        Assert.Null(service.FindConverter("wav", "mp3"));
        Assert.Null(service.FindConverter("docx", "pdf"));
    }

    [Fact]
    public void ReportsWhichToolsWouldUnlockMore()
    {
        var service = WithoutTools();
        Assert.Equal([ExternalTool.Pandoc, ExternalTool.LibreOffice], service.MissingToolsFor("report.docx"));
        Assert.Equal([ExternalTool.Ffmpeg], service.MissingToolsFor("clip.mp4"));
        Assert.Empty(service.MissingToolsFor("photo.png"));
    }

    [Fact]
    public void GifToGifIsAnImageJobButGifToMp4IsVideo()
    {
        var service = new ConversionService(new ToolLocator());
        Assert.IsType<ImageConverter>(service.FindConverter("gif", "gif"));
        if (service.IsInstalled(ExternalTool.Ffmpeg))
            Assert.IsType<FfmpegConverter>(service.FindConverter("gif", "mp4"));
    }
}
