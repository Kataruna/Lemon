namespace Lemon.Core.Conversion.Converters;

public sealed record ConversionRequest(string InputPath, string OutputPath, Preset Preset)
{
    public string SourceExtension => FormatCatalog.NormalizeExtension(InputPath);
    /// <summary>The output's extension. Optimize keeps the source's.</summary>
    public string TargetExtension => Preset.TargetExtension == Preset.SameFormat ? SourceExtension : FormatCatalog.NormalizeExtension(Preset.TargetExtension);
}

public interface IConverter
{
    string Name { get; }

    /// <summary>The external program this converter needs, or null if it's self-contained.</summary>
    ExternalTool? RequiredTool { get; }

    /// <summary>Whether this converter handles the pair at all, regardless of whether its tool is installed.</summary>
    bool Supports(string sourceExtension, string targetExtension);

    /// <summary>Whether it can honor this preset's options for the pair, e.g. a lossless Optimize of a video can't be.</summary>
    bool Supports(string sourceExtension, Preset preset) => true;

    /// <summary>
    /// Writes <see cref="ConversionRequest.OutputPath"/>. Progress is reported from 0 to 1 when known.
    /// When optimizing, leaving the output empty means "no worthwhile smaller version", and the original is kept.
    /// </summary>
    Task ConvertAsync(ConversionRequest request, IProgress<double>? progress, CancellationToken cancellationToken);
}
