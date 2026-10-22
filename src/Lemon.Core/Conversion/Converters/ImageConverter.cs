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
        // Output only: reading PDFs needs Ghostscript, which ImageMagick doesn't bundle.
        ["pdf"] = MagickFormat.Pdf,
    };

    /// <summary>Longest side of a PDF page made from an image, in inches (A4's height).</summary>
    private const double MaxPageInches = 11.69;

    /// <summary>Sources whose pixels are already lossy, so a JPEG-compressed PDF page loses nothing visible.</summary>
    private static readonly HashSet<string> PhotoSources = ["jpg", "jpeg", "webp", "avif", "heic", "heif"];

    private static readonly HashSet<MagickFormat> Readable = SupportedWhere(f => f.SupportsReading);
    private static readonly HashSet<MagickFormat> Writable = SupportedWhere(f => f.SupportsWriting);

    public string Name => "ImageMagick";
    public ExternalTool? RequiredTool => null;

    public bool Supports(string sourceExtension, string targetExtension) =>
        sourceExtension != "pdf" && Formats.TryGetValue(sourceExtension, out var from) && Readable.Contains(from)
        && Formats.TryGetValue(targetExtension, out var to) && Writable.Contains(to);

    // Remove background presets belong to BackgroundRemovalConverter.
    public bool Supports(string sourceExtension, Preset preset) => preset.RemoveBackground is null;

    public Task ConvertAsync(ConversionRequest request, IProgress<double>? progress, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var format = Formats[request.TargetExtension];
            try
            {
                if (format == MagickFormat.Pdf)
                    ConvertToPdf(request);
                else if (IsAnimatable(request.SourceExtension) && IsAnimatable(request.TargetExtension))
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

    /// <summary>One page per image; a multi-page TIFF becomes a multi-page PDF.</summary>
    private static void ConvertToPdf(ConversionRequest request)
    {
        using var pages = new MagickImageCollection();
        if (request.SourceExtension is "tif" or "tiff")
            pages.Read(request.InputPath);
        else
            pages.Add(new MagickImage(request.InputPath));

        foreach (var page in pages)
        {
            page.AutoOrient();
            Prepare(page, request.Preset, MagickFormat.Pdf);
            FitPage(page);
            if (PhotoSources.Contains(request.SourceExtension))
            {
                page.Settings.Compression = CompressionMethod.JPEG;
                page.Quality = (uint)Math.Clamp(request.Preset.Quality ?? 92, 1, 100);
            }
            else
            {
                // Screenshots, scans and graphics stay lossless.
                page.Settings.Compression = CompressionMethod.Zip;
            }
        }
        pages.Write(request.OutputPath, MagickFormat.Pdf);
    }

    /// <summary>
    /// Page size comes from the image's resolution. Keep it when the page fits on A4; otherwise (including the
    /// 72 dpi assumed for images without one, which turns a phone photo into a 56-inch page) scale it down to fit.
    /// </summary>
    private static void FitPage(IMagickImage<byte> page)
    {
        var density = page.Density.Units == DensityUnit.PixelsPerCentimeter
            ? page.Density.X * 2.54
            : page.Density.X > 0 ? page.Density.X : 72;
        var fitting = Math.Max(page.Width, page.Height) / MaxPageInches;
        var dpi = Math.Max(density, fitting);
        page.Density = new Density(dpi, dpi, DensityUnit.PixelsPerInch);
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
