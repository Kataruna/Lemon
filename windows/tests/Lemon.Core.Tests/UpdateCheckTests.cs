namespace Lemon.Core.Tests;

public class UpdateCheckTests
{
    private static string Releases(params (string Tag, bool Draft, bool Prerelease)[] releases) =>
        "[" + string.Join(",", releases.Select(r =>
            $$"""{"tag_name":"{{r.Tag}}","draft":{{(r.Draft ? "true" : "false")}},"prerelease":{{(r.Prerelease ? "true" : "false")}},"html_url":"https://example.com/{{r.Tag}}"}""")) + "]";

    [Fact]
    public void FindsTheNewestWindowsRelease()
    {
        var json = Releases(("windows-v0.3.0", false, false), ("windows-v0.4.0", false, false), ("windows-v0.2.0", false, false));
        var release = UpdateCheck.Newest(json, new Version(0, 3, 0, 0));
        Assert.Equal(new Version(0, 4, 0), release?.Version);
        Assert.Equal("https://example.com/windows-v0.4.0", release?.Url);
    }

    [Fact]
    public void IgnoresMacReleasesDraftsAndPrereleases()
    {
        var json = Releases(("mac-v9.0.0", false, false), ("windows-v1.0.0", true, false), ("windows-v0.9.0", false, true), ("windows-v0.3.0", false, false));
        Assert.Null(UpdateCheck.Newest(json, new Version(0, 3, 0, 0)));
    }

    [Fact]
    public void TheSameVersionIsNotAnUpdate()
    {
        // The assembly's four-part version and the tag's three parts still count as equal.
        Assert.Null(UpdateCheck.Newest(Releases(("windows-v0.3.0", false, false)), new Version(0, 3, 0, 0)));
        Assert.Null(UpdateCheck.Newest(Releases(("windows-v0.3", false, false)), new Version(0, 3, 0, 0)));
    }
}
