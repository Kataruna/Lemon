namespace Lemon.Core.Wheel;

/// <summary>The wheel's rings: formats to convert to, Optimize strengths, Remove background models and image sizes.</summary>
public enum WheelPage
{
    Convert,
    Optimize,
    RemoveBackground,
    Resize,
}

/// <summary>Decides which presets appear on the conversion wheel for a set of dragged files.</summary>
public static class ConversionWheel
{
    public const int MaxChoices = 8;

    /// <summary>The most useful built-in presets per kind of file, in the order they go around the wheel.</summary>
    private static readonly Dictionary<MediaKind, string[]> Favorites = new()
    {
        [MediaKind.Image] = ["img-png", "img-jpg", "img-webp", "doc-pdf", "img-avif", "img-ico", "img-gif", "img-tiff", "img-bmp"],
        [MediaKind.Audio] = ["aud-mp3-320", "aud-m4a", "aud-flac", "aud-wav", "aud-opus", "aud-ogg", "aud-mp3-192", "aud-aiff"],
        [MediaKind.Video] = ["vid-mp4", "vid-mp4-720", "vid-webm", "vid-gif", "aud-mp3-320", "vid-mov", "vid-mkv", "aud-m4a"],
        [MediaKind.Document] = ["doc-pdf", "doc-docx", "doc-html", "doc-txt", "doc-md", "doc-odt", "doc-rtf", "doc-epub"],
    };

    /// <summary>Optimize strengths go around their ring from gentlest to strongest.</summary>
    private static readonly string[] OptimizeOrder = ["optimize-lossless", BuiltInPresets.OptimizeId, "optimize-smaller"];

    private static readonly string[] RemoveBackgroundOrder = ["img-nobg", "img-nobg-full"];

    /// <summary>Sizes go around their ring from largest to smallest.</summary>
    private static readonly string[] ResizeOrder = ["resize-4k", "resize-fhd", "resize-hd", "resize-sd"];

    /// <summary>The ring a preset belongs on.</summary>
    public static WheelPage PageOf(Preset preset) =>
        preset.IsOptimize ? WheelPage.Optimize
        : preset.IsResize ? WheelPage.Resize
        : preset.RemoveBackground is not null ? WheelPage.RemoveBackground
        : WheelPage.Convert;

    /// <summary>
    /// Picks up to <see cref="MaxChoices"/> presets that work for every dragged file: the user's own presets
    /// first, then the favorites for <paramref name="kind"/>, then anything else that fits.
    /// </summary>
    /// <param name="availablePerFileType">The presets usable for each distinct dragged file type.</param>
    /// <param name="kind">The kind of the dragged files (the most common one when they're mixed).</param>
    /// <param name="page">Format presets for the Convert ring, Optimize presets for the Optimize ring.</param>
    public static IReadOnlyList<Preset> Choose(
        IReadOnlyList<IReadOnlyList<Preset>> availablePerFileType, MediaKind kind, WheelPage page = WheelPage.Convert)
    {
        if (availablePerFileType.Count == 0)
            return [];

        var favorites = page switch
        {
            WheelPage.Optimize => OptimizeOrder,
            WheelPage.RemoveBackground => RemoveBackgroundOrder,
            WheelPage.Resize => ResizeOrder,
            _ => Favorites[kind],
        };
        return availablePerFileType[0]
            .Where(p => PageOf(p) == page)
            .Where(p => availablePerFileType.Skip(1).All(list => list.Any(q => q.Id == p.Id)))
            .OrderBy(p => p.IsBuiltIn)
            .ThenBy(p => Array.IndexOf(favorites, p.Id) is var i and >= 0 ? i : int.MaxValue)
            .Take(MaxChoices)
            .ToList();
    }

    /// <summary>
    /// Like <see cref="Choose(IReadOnlyList{IReadOnlyList{Preset}}, MediaKind)"/>, but a drag that mixes kinds of
    /// files (say photos and a video) isn't left with an empty wheel: presets that fit every file come first, and
    /// the free slots go to presets that fit all files of the main <paramref name="kind"/>.
    /// </summary>
    public static IReadOnlyList<Preset> ChooseByKind(
        IReadOnlyList<(MediaKind Kind, IReadOnlyList<Preset> Presets)> availablePerFileType, MediaKind kind, WheelPage page = WheelPage.Convert)
    {
        var all = availablePerFileType.Select(t => t.Presets).ToList();
        var main = availablePerFileType.Where(t => t.Kind == kind).Select(t => t.Presets).ToList();
        return Choose(all, kind, page).Concat(Choose(main, kind, page)).DistinctBy(p => p.Id).Take(MaxChoices).ToList();
    }

    /// <summary>
    /// Two-line wheel label: the short format ("MP4") over what makes the preset different ("720p small").
    /// </summary>
    public static (string Title, string? Detail) Label(Preset preset)
    {
        if (preset.IsOptimize)
        {
            return preset.OptimizeStrength switch
            {
                OptimizeStrength.Lossless => ("Lossless", "Same pixels"),
                OptimizeStrength.Smaller => ("Smaller", "Slightly lower quality"),
                _ => ("Balanced", "Looks the same"),
            };
        }

        if (preset.IsResize)
            return (preset.Name.Split(" – ", 2, StringSplitOptions.TrimEntries)[^1], $"{preset.MaxWidth ?? preset.MaxHeight} px");

        if (preset.RemoveBackground is { } model)
            return model == BackgroundModel.Lite ? ("Lite", "Remove background") : ("Full", "Best quality");

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
