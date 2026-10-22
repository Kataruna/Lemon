using ImageMagick;

namespace Lemon.Core.Conversion.Converters;

/// <summary>Still and animated image conversion using the bundled ImageMagick (Magick.NET).</summary>
public sealed class ImageConverter : IConverter
{
    private static readonly Dictionary<string, MagickFormat> Formats = new()
    {
        ["png"] = MagickFormat.Png,
        ["jpg"] = MagickFormat.Jpeg,
        ["jpeg"] = MagickFormat.Jpeg,
        ["webp"] = MagickFormat.WebP,
        ["avif"] = MagickFormat.Avif,
        ["heic"] = MagickFormat.Heic,
        ["heif"] = MagickFormat.Heic,
        ["gif"] = MagickFormat.Gif,
        ["bmp"] = MagickFormat.Bmp,
        ["tif"] = MagickFormat.Tiff,
        ["tiff"] = MagickFormat.Tiff,
        ["ico"] = MagickFormat.Ico,
    };

    private static readonly HashSet<MagickFormat> Readable = SupportedWhere(f => f.SupportsReading);
    private static readonly HashSet<MagickFormat> Writable = SupportedWhere(f => f.SupportsWriting);

    public string Name => "ImageMagick";
    public ExternalTool? RequiredTool => null;

    public bool Supports(string sourceExtension, string targetExtension) =>
        Formats.TryGetValue(sourceExtension, out var from) && Readable.Contains(from)
        && Formats.TryGetValue(targetExtension, out var to) && Writable.Contains(to);

    public Task ConvertAsync(ConversionRequest request, IProgress<double>? progress, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var format = Formats[request.TargetExtension];
            try
            {
                if (IsAnimatable(request.SourceExtension) && IsAnimatable(request.TargetExtension))
                    ConvertAnimated(request, format);
                else
                    ConvertStill(request, format);
            }
            catch (MagickException ex)
            {
                throw new ConversionException(ex.Message);
            }
            progress?.Report(1);
        }, cancellationToken);

    private static void ConvertStill(ConversionRequest request, MagickFormat format)
    {
        using var image = new MagickImage(request.InputPath);
        image.AutoOrient();
        Prepare(image, request.Preset, format);
        image.Write(request.OutputPath, format);
    }

    private static void ConvertAnimated(ConversionRequest request, MagickFormat format)
    {
        using var frames = new MagickImageCollection(request.InputPath);
        frames.Coalesce();
        foreach (var frame in frames)
            Prepare(frame, request.Preset, format);
        if (format == MagickFormat.Gif)
            frames.Optimize();
        frames.Write(request.OutputPath, format);
    }

    private static void Prepare(IMagickImage<byte> image, Preset preset, MagickFormat format)
    {
        // ICO tops out at 256x256 no matter what the preset says.
        var maxWidth = format == MagickFormat.Ico ? Math.Min(preset.MaxWidth ?? 256, 256) : preset.MaxWidth;
        var maxHeight = format == MagickFormat.Ico ? Math.Min(preset.MaxHeight ?? 256, 256) : preset.MaxHeight;
        if (maxWidth is not null || maxHeight is not null)
        {
            var geometry = new MagickGeometry((uint)(maxWidth ?? int.MaxValue), (uint)(maxHeight ?? int.MaxValue)) { Greater = true };
            image.Resize(geometry);
        }

        if (preset.Quality is { } quality)
            image.Quality = (uint)Math.Clamp(quality, 1, 100);

        if (format is MagickFormat.Jpeg or MagickFormat.Bmp && image.HasAlpha)
        {
            // Formats without transparency would otherwise turn see-through pixels black.
            image.BackgroundColor = MagickColors.White;
            image.Alpha(AlphaOption.Remove);
        }

        if (preset.StripMetadata)
            image.Strip();
    }

    private static bool IsAnimatable(string extension) => extension is "gif" or "webp";

    private static HashSet<MagickFormat> SupportedWhere(Func<IMagickFormatInfo, bool> predicate) =>
        MagickNET.SupportedFormats.Where(predicate).Select(f => f.Format).ToHashSet();
}
