namespace Lemon.Core.Tests;

public class FormatCatalogTests
{
    [Theory]
    [InlineData(".JPG", "jpg")]
    [InlineData("jpg", "jpg")]
    [InlineData(@"C:\My Photos\Holiday.Final.HEIC", "heic")]
    public void NormalizesExtensions(string input, string expected) =>
        Assert.Equal(expected, FormatCatalog.NormalizeExtension(input));

    [Fact]
    public void KnowsKinds()
    {
        Assert.Equal(MediaKind.Video, FormatCatalog.KindOf("clip.mkv"));
        Assert.Equal(MediaKind.Document, FormatCatalog.KindOf("notes.md"));
        Assert.Null(FormatCatalog.KindOf("archive.zip"));
    }
}
