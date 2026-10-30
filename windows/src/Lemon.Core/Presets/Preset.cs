using System.Text.Json.Serialization;

namespace Lemon.Core.Presets;

/// <summary>How hard Optimize may squeeze a file.</summary>
public enum OptimizeStrength
{
    /// <summary>Only changes that keep every pixel (images only).</summary>
    Lossless,

    /// <summary>Lighter encodes that still look the same as the original.</summary>
    Balanced,

    /// <summary>Like Clop's aggressive mode: slightly lower quality for a noticeably smaller file.</summary>
    Smaller,
}

/// <summary>A named set of conversion settings. Unset values mean "keep the source's value".</summary>
public sealed record Preset
{
    /// <summary>
    /// <see cref="TargetExtension"/> of the Optimize preset: keep the source's format and only make the file smaller.
    /// </summary>
    public const string SameFormat = "*";

    public string Id { get; init; } = Guid.NewGuid().ToString("n");
    public required string Name { get; init; }
    public required string TargetExtension { get; init; }
    public bool IsBuiltIn { get; init; }

    /// <summary>Image encoder quality, 1-100.</summary>
    public int? Quality { get; init; }
    public int? MaxWidth { get; init; }
    public int? MaxHeight { get; init; }
    public int? AudioBitrateKbps { get; init; }

    /// <summary>x264/VP9 constant rate factor. Lower is better quality.</summary>
    public int? VideoCrf { get; init; }
    public int? Fps { get; init; }
    public bool StripMetadata { get; init; }

    /// <summary>Set for Remove background presets (which write PNG): the AI model that cuts the subject out.</summary>
    public BackgroundModel? RemoveBackground { get; init; }

    /// <summary>Set for Upscale presets (a kind of Resize): the AI model that enlarges the image <see cref="Scale"/> times.</summary>
    public UpscaleModel? Upscale { get; init; }

    /// <summary>Only for Upscale presets: 2 or 4.</summary>
    public int? Scale { get; init; }

    /// <summary>Set on the Commands ring's slices: the <see cref="Wheel.WheelCommand.Id"/> a drop there runs.</summary>
    [JsonIgnore]
    public string? CommandId { get; init; }

    /// <summary>Only for Optimize presets.</summary>
    public OptimizeStrength OptimizeStrength { get; init; } = OptimizeStrength.Balanced;

    /// <summary>Optimize mode: same format, smaller file, quality kept close to the original.</summary>
    [JsonIgnore]
    public bool IsOptimize => TargetExtension == SameFormat && !IsResize;

    /// <summary>
    /// Resize mode (images only): same format, shrunk to fit <see cref="MaxWidth"/> × <see cref="MaxHeight"/>,
    /// or enlarged by <see cref="Upscale"/>.
    /// </summary>
    [JsonIgnore]
    public bool IsResize => TargetExtension == SameFormat && (MaxWidth is not null || MaxHeight is not null || Upscale is not null);

    /// <summary>A format change and nothing else special: what "To PNG" means, unlike a cutout that also writes PNG.</summary>
    [JsonIgnore]
    public bool IsPlainConversion => RemoveBackground is null && Upscale is null && !IsResize;

    [JsonIgnore]
    public MediaKind TargetKind => FormatCatalog.KindOf(TargetExtension) ?? MediaKind.Document;

    /// <summary>What the preset does, e.g. "WebP · quality 80 · up to 1920 px", for preset lists.</summary>
    [JsonIgnore]
    public string Summary => Upscale is not null
        ? $"Same format, {Scale}× larger"
        : IsResize
        ? $"Same format, up to {MaxWidth ?? MaxHeight} px"
        : IsOptimize
        ? "Same format, smaller file"
        : string.Join(" · ", SummaryParts());

    private IEnumerable<string> SummaryParts()
    {
        yield return FormatCatalog.Find(TargetExtension)?.DisplayName ?? TargetExtension.ToUpperInvariant();
        if (Quality is { } quality)
            yield return $"quality {quality}";
        if ((MaxWidth ?? MaxHeight) is { } size)
            yield return $"up to {size} px";
        if (AudioBitrateKbps is { } kbps)
            yield return $"{kbps} kbps";
        if (VideoCrf is { } crf)
            yield return $"CRF {crf}";
        if (Fps is { } fps)
            yield return $"{fps} fps";
        if (StripMetadata)
            yield return "no metadata";
        if (Quality is null && MaxWidth is null && MaxHeight is null && AudioBitrateKbps is null && VideoCrf is null && Fps is null && !StripMetadata)
            yield return "default settings";
    }

    /// <summary>What screen readers announce for a preset in a list.</summary>
    public override string ToString() => Name;
}
