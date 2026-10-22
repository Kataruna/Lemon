namespace Lemon.Core.Conversion;

/// <summary>Picks a converter for each job, reserves the output file and cleans up after failures.</summary>
public sealed class ConversionService
{
    private readonly ToolLocator _tools;
    private readonly Dictionary<ExternalTool, bool> _available = [];
    private readonly Lock _gate = new();

    public ConversionService(ToolLocator tools)
    {
        _tools = tools;
        Converters =
        [
            new ImageConverter(),
            new FfmpegConverter(tools),
            new MarkdownConverter(),
            new PandocConverter(tools),
            new LibreOfficeConverter(tools),
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

    public bool CanConvert(string sourcePath, Preset preset) => FindConverter(sourcePath, preset.TargetExtension) is not null;

    /// <summary>Tools that aren't installed but would unlock more targets for this kind of file.</summary>
    public IEnumerable<ExternalTool> MissingToolsFor(string sourcePath)
    {
        var from = FormatCatalog.NormalizeExtension(sourcePath);
        return Converters
            .Where(c => c.RequiredTool is { } tool && !IsInstalled(tool)
                        && FormatCatalog.Extensions.Any(to => c.Supports(from, to) && FindConverter(from, to) is null))
            .Select(c => c.RequiredTool!.Value)
            .Distinct();
    }

    /// <summary>Converts one file and returns the path of the result.</summary>
    public async Task<string> ConvertAsync(
        string inputPath,
        Preset preset,
        string? outputFolder,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(inputPath))
            throw new ConversionException("The file no longer exists.");

        var converter = FindConverter(inputPath, preset.TargetExtension)
            ?? throw new ConversionException($"Can't convert {FormatCatalog.NormalizeExtension(inputPath).ToUpperInvariant()} to {preset.TargetExtension.ToUpperInvariant()}.");

        var outputPath = OutputPaths.Reserve(inputPath, preset.TargetExtension, outputFolder);
        try
        {
            await converter.ConvertAsync(new ConversionRequest(inputPath, outputPath, preset), progress, cancellationToken);
            if (new FileInfo(outputPath).Length == 0)
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
