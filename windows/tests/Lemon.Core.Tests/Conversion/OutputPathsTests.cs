namespace Lemon.Core.Tests;

public class OutputPathsTests
{
    [Fact]
    public void NeverOverwritesAndNeverReturnsTheInput()
    {
        using var temp = new TempFolder();
        var input = temp.File("song.mp3");
        File.WriteAllText(input, "x");

        Assert.Equal(temp.File("song (1).mp3"), OutputPaths.Reserve(input, "mp3"));
        Assert.Equal(temp.File("song.wav"), OutputPaths.Reserve(input, "wav"));
        Assert.Equal(temp.File("song (1).wav"), OutputPaths.Reserve(input, ".WAV"));
    }

    [Fact]
    public void UsesCustomOutputFolder()
    {
        using var temp = new TempFolder();
        var input = temp.File("a.png");
        var output = OutputPaths.Reserve(input, "jpg", temp.File("out"));
        Assert.Equal(temp.File(@"out\a.jpg"), output);
        Assert.True(File.Exists(output));
    }

    [Fact]
    public void ParallelReservationsAreUnique()
    {
        using var temp = new TempFolder();
        var input = temp.File("a.png");
        var paths = Enumerable.Range(0, 20).AsParallel().Select(_ => OutputPaths.Reserve(input, "jpg")).ToList();
        Assert.Equal(20, paths.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}
