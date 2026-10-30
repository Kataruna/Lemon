namespace Lemon.Core.Tests;

public class LaunchRequestTests
{
    [Fact]
    public void ParsesFilesAndQuickTarget()
    {
        var request = LaunchRequest.Parse(["--to", ".PNG", @"C:\a b\x.heic", @"C:\y.jpg"]);
        Assert.Equal("png", request.QuickTarget);
        Assert.Equal([@"C:\a b\x.heic", @"C:\y.jpg"], request.Files);
    }

    [Fact]
    public void OptimizeFlagPicksTheSameFormatTarget()
    {
        var request = LaunchRequest.Parse(["--optimize", @"C:\shot.png"]);
        Assert.Equal(Preset.SameFormat, request.QuickTarget);
        Assert.Equal([@"C:\shot.png"], request.Files);
        Assert.True(request.StaysInBackground);
    }

    [Fact]
    public void PlainLaunchIsEmpty() => Assert.True(LaunchRequest.Parse([]).IsEmpty);

    [Fact]
    public void BackgroundAndQuickConvertLaunchesStayHidden()
    {
        Assert.True(LaunchRequest.Parse(["--background"]).StaysInBackground);
        Assert.True(LaunchRequest.Parse(["--to", "png", "a.jpg"]).StaysInBackground);
        Assert.False(LaunchRequest.Parse(["a.jpg"]).StaysInBackground);
    }

    [Fact]
    public void PresetLaunchNamesThePresetAndStaysHidden()
    {
        var request = LaunchRequest.Parse(["--preset", "upscale-2x", @"C:\shot.png"]);
        Assert.Equal("upscale-2x", request.PresetId);
        Assert.Equal([@"C:\shot.png"], request.Files);
        Assert.True(request.StaysInBackground);
    }
}
