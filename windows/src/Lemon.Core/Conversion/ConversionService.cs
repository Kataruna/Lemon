namespace Lemon.Core.Conversion;

/// <summary>Picks a converter for each job, reserves the output file and cleans up after failures.</summary>
public sealed class ConversionService
{
    /// <summary>Added to optimized copies: "photo.jpg" becomes "photo (optimized).jpg".</summary>
    public const string OptimizedSuffix = " (optimized)";

    private readonly ToolLocator _tools;
    private readonly Dictionary<ExternalTool, bool> _available = [];
    private readonly Lock _gate = new();

    public ConversionService(ToolLocator tools)
    {
        _tools = tools;
        Converters =
        [
            new ImageConverter(),
            new BackgroundRemovalConverter(),
            new UpscaleConverter(),
            new FfmpegConverter(tools),
            new MarkdownConverter(),
            new PandocConverter(tools),
            new LibreOfficeConverter(tools),
            new ImageOptimizeConverter(),
            new VideoOptimizeConverter(tools),
            new PdfOptimizeConverter(tools),
        ];
    }

    /// <summary>In order of preference: earlier converters win when several can do the same job.</summary>
    public IReadOnlyList<IConverter> Converters { get; }

    public bool IsInstalled(ExternalTool tool)
    {
        lock (_gate)
        {
            if (!_available.TryGetValue(tool, out var found))
                _available[tool] = found = _tools.Find(tool) is not null;
            return found;
        }
    }

    /// <summary>Forget which tools were found, e.g. after the user installs one or changes a tool path.</summary>
    public void RefreshTools()
    {
        lock (_gate)
            _available.Clear();
    }

    public IConverter? FindConverter(string sourceExtension, string targetExtension)
    {
        var from = FormatCatalog.NormalizeExtension(sourceExtension);
        var to = FormatCatalog.NormalizeExtension(targetExtension);
        return Converters.FirstOrDefault(c => c.Supports(from, to) && (c.RequiredTool is not { } tool || IsInstalled(tool)));
    }

    /// <summary>Like <see cref="FindConverter(string, string)"/>, but also checks the converter can honor the preset's options.</summary>
    public IConverter? FindConverter(string sourcePath, Preset preset)
    {
        var from = FormatCatalog.NormalizeExtension(sourcePath);
        // Resize re-encodes in the source's own format, and is only offered for images.
        if (preset.IsResize && FormatCatalog.KindOf(from) != MediaKind.Image)
            return null;
        var to = preset.IsResize ? from : FormatCatalog.NormalizeExtension(preset.TargetExtension);
        return Converters.FirstOrDefault(c => c.Supports(from, to) && c.Supports(from, preset)
                                              && (c.RequiredTool is not { } tool || IsInstalled(tool)));
    }

    public bool CanConvert(string sourcePath, Preset preset) => FindConverter(sourcePath, preset) is not null;

    /// <summary>Tools that aren't installed but would unlock more targets for this kind of file.</summary>
    public IEnumerable<ExternalTool> MissingToolsFor(string sourcePath)
    {
        var from = FormatCatalog.NormalizeExtension(sourcePath);
        return Converters
            .Where(c => c.RequiredTool is { } tool && !IsInstalled(tool)
                        && FormatCatalog.Extensions.Append(Preset.SameFormat).Any(to => c.Supports(from, to) && FindConverter(from, to) is null))
            .Select(c => c.RequiredTool!.Value)
            .Distinct();
    }

    /// <summary>
    /// Converts one file and returns the path of the result. When optimizing doesn't find a smaller version
    /// worth keeping, or the image already fits a Resize preset, nothing is written and <paramref name="inputPath"/>
    /// itself is returned.
    /// </summary>
    public async Task<string> ConvertAsync(
        string inputPath,
        Preset preset,
        string? outputFolder,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(inputPath))
            throw new ConversionException("The file no longer exists.");

        var source = FormatCatalog.NormalizeExtension(inputPath);
        var converter = FindConverter(inputPath, preset)
            ?? throw new ConversionException(preset.IsOptimize
                ? $"Can't optimize {source.ToUpperInvariant()} files."
                : preset.IsResize ? $"Can't resize {source.ToUpperInvariant()} files."
                : $"Can't convert {source.ToUpperInvariant()} to {preset.TargetExtension.ToUpperInvariant()}.");

        // Resize only ever shrinks (Upscale aside); re-encoding an image that already fits would just cost quality.
        if (preset.IsResize && preset.Upscale is null && ImageConverter.Fits(inputPath, preset))
            return inputPath;

        var outputPath = preset.IsOptimize
            ? OutputPaths.Reserve(inputPath, source, outputFolder, OptimizedSuffix)
            : preset.Upscale is not null
            ? OutputPaths.Reserve(inputPath, source, outputFolder, $" ({preset.Scale}×)")
            : preset.IsResize
            ? OutputPaths.Reserve(inputPath, source, outputFolder, $" ({preset.MaxWidth ?? preset.MaxHeight} px)")
            : OutputPaths.Reserve(inputPath, preset.TargetExtension, outputFolder, preset.RemoveBackground is null ? null : BackgroundRemovalConverter.Suffix);
        try
        {
            await converter.ConvertAsync(new ConversionRequest(inputPath, outputPath, preset), progress, cancellationToken);
            var length = new FileInfo(outputPath).Length;
            if (preset.IsOptimize && (length == 0 || length >= new FileInfo(inputPath).Length))
            {
                // Never hand back a copy that's no smaller; the original already is the best version.
                TryDelete(outputPath);
                return inputPath;
            }
            if (length == 0)
                throw new ConversionException($"{converter.Name} produced an empty file.");
            return outputPath;
        }
        catch
        {
            TryDelete(outputPath);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // The tool may still hold the file for a moment after being killed; leaving it is harmless.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
