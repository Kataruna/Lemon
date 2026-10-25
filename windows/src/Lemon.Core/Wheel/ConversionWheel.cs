namespace Lemon.Core.Wheel;

/// <summary>The wheel's rings: formats to convert to, Optimize strengths, Remove background models and image sizes.</summary>
public enum WheelPage
{
    Convert,
    Optimize,
    RemoveBackground,
    Resize,
}

/// <summary>One of the wheel's rings: built in (<see cref="Page"/>), or put together by the user (<see cref="PresetIds"/>).</summary>
/// <param name="Glyph">Segoe Fluent Icons glyph.</param>
/// <param name="Detail">One line about the ring, for its slice on the hub.</param>
public sealed record WheelRing(string Id, string Name, string Glyph, string Detail, WheelPage? Page, IReadOnlyList<string>? PresetIds = null)
{
    /// <summary>The user's commands. Not one of the built-ins Settings can hide: no commands, no ring.</summary>
    public static readonly WheelRing Commands = new("commands", "Commands", "", "Your commands", null, []);

    public static readonly IReadOnlyList<WheelRing> BuiltIns =
    [
        new("convert", "Convert", "\uE8AB", "Change format", WheelPage.Convert),
        new("optimize", "Optimize", "\uE73F", "Make smaller", WheelPage.Optimize),
        new("cutout", "Cut out", "\uE8C6", "Remove background", WheelPage.RemoveBackground),
        new("resize", "Resize", "\uE740", "Shrink images", WheelPage.Resize),
    ];

    /// <summary>The built-in rings, then the user's own in Settings order; hidden ones included.</summary>
    public static IEnumerable<WheelRing> All(WheelSettings settings) =>
        BuiltIns.Concat(settings.Rings.Select(r => new WheelRing(r.Id, r.Name, r.Glyph, "Your ring", null, r.PresetIds)));

    public bool Holds(Preset preset) => PresetIds?.Contains(preset.Id) ?? ConversionWheel.PageOf(preset) == Page;
}

/// <summary>The rings a drag gets: the ones switched on that have something for the files, with their presets.</summary>
/// <param name="Start">Index of the ring the wheel opens on (when it doesn't open on the hub).</param>
/// <param name="HubOn">The hub, where the rings themselves are the slices, is there to switch rings.</param>
/// <param name="OpensOnHub">The wheel opens on the hub rather than on <paramref name="Start"/>.</param>
public sealed record WheelPlan(IReadOnlyList<WheelRing> Rings, IReadOnlyList<IReadOnlyList<Preset>> Choices, int Start, bool HubOn, bool OpensOnHub)
{
    public static readonly WheelPlan Empty = new([], [], 0, false, false);
}

/// <summary>Decides which presets appear on the conversion wheel for a set of dragged files.</summary>
public static class ConversionWheel
{
    public const int MaxChoices = 8;

    /// <summary>How many rings fit on the mode bar under the wheel; with more, the hub takes over switching.</summary>
    public const int MaxBarRings = 4;

    /// <summary>The Convert ring for a kind of file until the user picks their own in Settings.</summary>
    public static IReadOnlyList<string> DefaultConvertRing(MediaKind kind) => Favorites[kind];

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
        IReadOnlyList<IReadOnlyList<Preset>> availablePerFileType, MediaKind kind, WheelPage page = WheelPage.Convert) =>
        Choose(availablePerFileType, kind, WheelRing.BuiltIns.Single(r => r.Page == page), convertRing: null);

    /// <param name="convertRing">The Convert ring the user picked for <paramref name="kind"/>, by preset id, or null.</param>
    public static IReadOnlyList<Preset> Choose(
        IReadOnlyList<IReadOnlyList<Preset>> availablePerFileType, MediaKind kind, WheelRing ring, IReadOnlyList<string>? convertRing)
    {
        if (availablePerFileType.Count == 0)
            return [];

        // Picked by hand (a ring of the user's own, or their Convert ring): only those, in their order.
        var picked = ring.PresetIds ?? (ring.Page == WheelPage.Convert ? convertRing : null);
        var order = (picked ?? (ring.Page switch
        {
            WheelPage.Optimize => OptimizeOrder,
            WheelPage.RemoveBackground => RemoveBackgroundOrder,
            WheelPage.Resize => ResizeOrder,
            _ => Favorites[kind],
        })).ToList();
        return availablePerFileType[0]
            .Where(ring.Holds)
            .Where(p => picked is null || picked.Contains(p.Id))
            .Where(p => availablePerFileType.Skip(1).All(list => list.Any(q => q.Id == p.Id)))
            .OrderBy(p => picked is null && p.IsBuiltIn)
            .ThenBy(p => order.IndexOf(p.Id) is var i and >= 0 ? i : int.MaxValue)
            .Take(MaxChoices)
            .ToList();
    }

    /// <summary>
    /// Like <see cref="Choose(IReadOnlyList{IReadOnlyList{Preset}}, MediaKind)"/>, but a drag that mixes kinds of
    /// files (say photos and a video) isn't left with an empty wheel: presets that fit every file come first, and
    /// the free slots go to presets that fit all files of the main <paramref name="kind"/>.
    /// </summary>
    public static IReadOnlyList<Preset> ChooseByKind(
        IReadOnlyList<(MediaKind Kind, IReadOnlyList<Preset> Presets)> availablePerFileType, MediaKind kind, WheelPage page = WheelPage.Convert) =>
        ChooseByKind(availablePerFileType, kind, WheelRing.BuiltIns.Single(r => r.Page == page), convertRing: null);

    public static IReadOnlyList<Preset> ChooseByKind(
        IReadOnlyList<(MediaKind Kind, IReadOnlyList<Preset> Presets)> availablePerFileType, MediaKind kind, WheelRing ring, IReadOnlyList<string>? convertRing)
    {
        var all = availablePerFileType.Select(t => t.Presets).ToList();
        var main = availablePerFileType.Where(t => t.Kind == kind).Select(t => t.Presets).ToList();
        return Choose(all, kind, ring, convertRing).Concat(Choose(main, kind, ring, convertRing)).DistinctBy(p => p.Id).Take(MaxChoices).ToList();
    }

    /// <summary>
    /// Works out a drag's rings from the user's wheel settings: the ones switched on that have something for these
    /// files, and where the wheel opens. That's the ring chosen for <paramref name="kind"/> (the first one if none
    /// was), or the hub when asked for or when the chosen ring has nothing for these files. With more rings than
    /// fit on the mode bar, the hub is how to switch.
    /// </summary>
    /// <param name="commands">The commands that have something to run on in this drag; up to 8 go on a Commands ring, last.</param>
    /// <param name="onCommands">Open on the Commands ring (a drag of only folders).</param>
    public static WheelPlan Plan(
        IReadOnlyList<(MediaKind Kind, IReadOnlyList<Preset> Presets)> availablePerFileType, MediaKind kind, WheelSettings settings,
        IReadOnlyList<WheelCommand>? commands = null, bool onCommands = false)
    {
        var convertRing = settings.ConvertRing.GetValueOrDefault(kind);
        var rings = new List<WheelRing>();
        var choices = new List<IReadOnlyList<Preset>>();
        foreach (var ring in WheelRing.All(settings).Where(r => !settings.HiddenRings.Contains(r.Id)))
        {
            var presets = ChooseByKind(availablePerFileType, kind, ring, convertRing);
            if (presets.Count == 0)
                continue;
            rings.Add(ring);
            choices.Add(presets);
        }
        if (commands is { Count: > 0 })
        {
            rings.Add(WheelRing.Commands);
            choices.Add(commands.Take(MaxChoices).Select(c => new Preset { Name = c.Name, TargetExtension = "", CommandId = c.Id }).ToList());
        }

        var start = onCommands && commands is { Count: > 0 } ? WheelRing.Commands.Id
            : settings.OpenOnHub ? "hub" : settings.OpensOn.GetValueOrDefault(kind);
        var index = rings.FindIndex(r => r.Id == start);
        var hubOn = rings.Count > 1 && (start == "hub" || rings.Count > MaxBarRings);
        return new WheelPlan(rings, choices, Math.Max(0, index), hubOn, hubOn && index < 0 && start is not null);
    }

    /// <summary>
    /// Two-line wheel label: the short format ("MP4") over what makes the preset different ("720p small").
    /// </summary>
    public static (string Title, string? Detail) Label(Preset preset)
    {
        if (preset.CommandId is not null)
            return (preset.Name, null);

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
