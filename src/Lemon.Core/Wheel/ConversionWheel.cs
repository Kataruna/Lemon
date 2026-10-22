namespace Lemon.Core.Wheel;

/// <summary>Decides which presets appear on the conversion wheel for a set of dragged files.</summary>
public static class ConversionWheel
{
    public const int MaxChoices = 8;

    /// <summary>The most useful built-in presets per kind of file, in the order they go around the wheel.</summary>
    private static readonly Dictionary<MediaKind, string[]> Favorites = new()
    {
        [MediaKind.Image] = ["img-png", "img-jpg", "img-webp", "img-avif", "img-ico", "img-gif", "img-tiff", "img-bmp"],
        [MediaKind.Audio] = ["aud-mp3-320", "aud-m4a", "aud-flac", "aud-wav", "aud-opus", "aud-ogg", "aud-mp3-192", "aud-aiff"],
        [MediaKind.Video] = ["vid-mp4", "vid-mp4-720", "vid-webm", "vid-gif", "aud-mp3-320", "vid-mov", "vid-mkv", "aud-m4a"],
        [MediaKind.Document] = ["doc-pdf", "doc-docx", "doc-html", "doc-txt", "doc-md", "doc-odt", "doc-rtf", "doc-epub"],
    };

    /// <summary>
    /// Picks up to <see cref="MaxChoices"/> presets that work for every dragged file: the user's own presets
    /// first, then the favorites for <paramref name="kind"/>, then anything else that fits.
    /// </summary>
    /// <param name="availablePerFileType">The presets usable for each distinct dragged file type.</param>
    /// <param name="kind">The kind of the dragged files (the most common one when they're mixed).</param>
    public static IReadOnlyList<Preset> Choose(IReadOnlyList<IReadOnlyList<Preset>> availablePerFileType, MediaKind kind)
    {
        if (availablePerFileType.Count == 0)
            return [];

        var favorites = Favorites[kind];
        return availablePerFileType[0]
            .Where(p => availablePerFileType.Skip(1).All(list => list.Any(q => q.Id == p.Id)))
            .OrderBy(p => p.IsBuiltIn)
            .ThenBy(p => Array.IndexOf(favorites, p.Id) is var i and >= 0 ? i : int.MaxValue)
            .Take(MaxChoices)
            .ToList();
    }

    /// <summary>
    /// Two-line wheel label: the short format ("MP4") over what makes the preset different ("720p small").
    /// </summary>
    public static (string Title, string? Detail) Label(Preset preset)
    {
        var extension = FormatCatalog.NormalizeExtension(preset.TargetExtension);
        var title = extension.ToUpperInvariant();
        var parts = preset.Name.Split(" – ", 2, StringSplitOptions.TrimEntries);
        if (parts.Length > 1)
            return (title, parts[1]);

        // Drop the format name the title already shows: "WebM (VP9)" -> "VP9", "Rich Text (RTF)" -> nothing.
        var detail = preset.Name;
        foreach (var prefix in new[] { FormatCatalog.Find(extension)?.DisplayName, title })
        {
            if (prefix is not null && detail.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                detail = detail[prefix.Length..].Trim(' ', '(', ')');
                break;
            }
        }
        return (title, detail.Length == 0 || detail.Equals(title, StringComparison.OrdinalIgnoreCase) ? null : detail);
    }
}
