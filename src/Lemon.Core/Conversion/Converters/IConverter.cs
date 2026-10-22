namespace Lemon.Core.Conversion.Converters;

public sealed record ConversionRequest(string InputPath, string OutputPath, Preset Preset)
{
    public string SourceExtension => FormatCatalog.NormalizeExtension(InputPath);
    public string TargetExtension => FormatCatalog.NormalizeExtension(Preset.TargetExtension);
}

public interface IConverter
{
    string Name { get; }

    /// <summary>The external program this converter needs, or null if it's self-contained.</summary>
    ExternalTool? RequiredTool { get; }

    /// <summary>Whether this converter handles the pair at all, regardless of whether its tool is installed.</summary>
    bool Supports(string sourceExtension, string targetExtension);

    /// <summary>Writes <see cref="ConversionRequest.OutputPath"/>. Progress is reported from 0 to 1 when known.</summary>
    Task ConvertAsync(ConversionRequest request, IProgress<double>? progress, CancellationToken cancellationToken);
}
