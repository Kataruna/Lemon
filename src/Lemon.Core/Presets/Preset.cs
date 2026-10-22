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

    /// <summary>Only for Optimize presets.</summary>
    public OptimizeStrength OptimizeStrength { get; init; } = OptimizeStrength.Balanced;

    /// <summary>Optimize mode: same format, smaller file, quality kept close to the original.</summary>
    [JsonIgnore]
    public bool IsOptimize => TargetExtension == SameFormat;

    [JsonIgnore]
    public MediaKind TargetKind => FormatCatalog.KindOf(TargetExtension) ?? MediaKind.Document;

    /// <summary>e.g. "WebP · Image · built-in", for preset lists.</summary>
    [JsonIgnore]
    public string Summary => IsOptimize
        ? "Same format, smaller file" + (IsBuiltIn ? " · built-in" : "")
        : $"{FormatCatalog.Find(TargetExtension)?.DisplayName ?? TargetExtension.ToUpperInvariant()} · {TargetKind}" + (IsBuiltIn ? " · built-in" : "");
}
