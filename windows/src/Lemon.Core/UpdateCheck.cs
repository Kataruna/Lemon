using System.Text.Json;

namespace Lemon.Core;

/// <summary>
/// Looks on GitHub for a newer Windows release of Lemon. The Mac and Windows apps are released separately, tagged
/// "mac-v…" and "windows-v…", so GitHub's single "latest" release could be the Mac one; this reads the list instead.
/// </summary>
public static class UpdateCheck
{
    private const string ReleasesApi = "https://api.github.com/repos/Kataruna/Lemon/releases?per_page=30";
    private const string TagPrefix = "windows-v";

    /// <param name="Url">The release's page on GitHub, with the downloads.</param>
    public sealed record Release(Version Version, string Url);

    /// <summary>The newest Windows release if it's newer than <paramref name="current"/>, else null.</summary>
    public static async Task<Release?> FindAsync(HttpClient http, Version current, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ReleasesApi);
        // GitHub's API turns away requests without a User-Agent.
        request.Headers.UserAgent.ParseAdd($"Lemon/{Short(current)}");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return Newest(await response.Content.ReadAsStringAsync(cancellationToken), current);
    }

    /// <summary>Picks from GitHub's release list: published (not draft or pre-release) Windows releases newer than <paramref name="current"/>.</summary>
    public static Release? Newest(string releasesJson, Version current)
    {
        current = Short(current);
        Release? newest = null;
        using var json = JsonDocument.Parse(releasesJson);
        foreach (var release in json.RootElement.EnumerateArray())
        {
            if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean())
                continue;
            var tag = release.GetProperty("tag_name").GetString() ?? "";
            if (!tag.StartsWith(TagPrefix, StringComparison.OrdinalIgnoreCase) || !Version.TryParse(tag[TagPrefix.Length..], out var version))
                continue;
            version = Short(version);
            if (version > current && (newest is null || version > newest.Version))
                newest = new Release(version, release.GetProperty("html_url").GetString() ?? "");
        }
        return newest;
    }

    /// <summary>
    /// Major.Minor.Patch: the assembly says 0.3.0.0 and a tag 0.3.0, and to <see cref="Version"/> those differ
    /// (a missing part counts as less than 0).
    /// </summary>
    private static Version Short(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build));
}
