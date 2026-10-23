using System.Net;
using Markdig;

namespace Lemon.Core.Conversion.Converters;

/// <summary>Markdown and plain-text conversions that need no external tools.</summary>
public sealed class MarkdownConverter : IConverter
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

    public string Name => "Built-in";
    public ExternalTool? RequiredTool => null;

    public bool Supports(string sourceExtension, string targetExtension) =>
        (sourceExtension, targetExtension) is ("md", "html") or ("md", "txt") or ("txt", "html") or ("txt", "md");

    public async Task ConvertAsync(ConversionRequest request, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var source = await File.ReadAllTextAsync(request.InputPath, cancellationToken);
        var title = Path.GetFileNameWithoutExtension(request.InputPath);

        var output = (request.SourceExtension, request.TargetExtension) switch
        {
            ("md", "html") => WrapHtml(title, Markdown.ToHtml(source, Pipeline)),
            ("md", "txt") => Markdown.ToPlainText(source, Pipeline),
            ("txt", "html") => WrapHtml(title, $"<pre>{WebUtility.HtmlEncode(source)}</pre>"),
            ("txt", "md") => source,
            _ => throw new ConversionException($"{Name} can't convert {request.SourceExtension} to {request.TargetExtension}."),
        };

        await File.WriteAllTextAsync(request.OutputPath, output, cancellationToken);
        progress?.Report(1);
    }

    private static string WrapHtml(string title, string body) => $$"""
        <!DOCTYPE html>
        <html>
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>{{WebUtility.HtmlEncode(title)}}</title>
        <style>
          body { font: 16px/1.6 system-ui, sans-serif; max-width: 46rem; margin: 2rem auto; padding: 0 1rem; color: #1f1f1f; }
          pre, code { font-family: Consolas, monospace; background: #f4f4f4; border-radius: 4px; }
          pre { padding: 1rem; overflow-x: auto; white-space: pre-wrap; }
          img { max-width: 100%; }
          table { border-collapse: collapse; }
          th, td { border: 1px solid #ddd; padding: 4px 8px; }
        </style>
        </head>
        <body>
        {{body}}
        </body>
        </html>
        """;
}

/// <summary>Text-document conversions via Pandoc (no PDF: Pandoc needs a LaTeX engine for that).</summary>
public sealed class PandocConverter(ToolLocator tools) : IConverter
{
    private static readonly Dictionary<string, string> Readers = new()
    {
        ["md"] = "markdown",
        ["txt"] = "markdown",
        ["html"] = "html",
        ["htm"] = "html",
        ["docx"] = "docx",
        ["odt"] = "odt",
        ["rtf"] = "rtf",
        ["epub"] = "epub",
    };

    private static readonly Dictionary<string, string> Writers = new()
    {
        ["md"] = "gfm",
        ["txt"] = "plain",
        ["html"] = "html5",
        ["docx"] = "docx",
        ["odt"] = "odt",
        ["rtf"] = "rtf",
        ["epub"] = "epub3",
    };

    public string Name => "Pandoc";
    public ExternalTool? RequiredTool => ExternalTool.Pandoc;

    public bool Supports(string sourceExtension, string targetExtension) =>
        Readers.ContainsKey(sourceExtension) && Writers.ContainsKey(targetExtension) && sourceExtension != targetExtension;

    public async Task ConvertAsync(ConversionRequest request, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var pandoc = tools.Find(ExternalTool.Pandoc) ?? throw new ConversionException("Pandoc isn't installed.");
        var title = Path.GetFileNameWithoutExtension(request.InputPath);

        var result = await ProcessRunner.RunAsync(
            pandoc,
            [
                request.InputPath,
                "--from", Readers[request.SourceExtension],
                "--to", Writers[request.TargetExtension],
                "--standalone",
                "--metadata", $"pagetitle={title}",
                "--resource-path", Path.GetDirectoryName(Path.GetFullPath(request.InputPath))!,
                "--output", request.OutputPath,
            ],
            cancellationToken: cancellationToken);

        if (result.ExitCode != 0)
            throw new ConversionException(ProcessRunner.Summarize(result.StandardError));
        progress?.Report(1);
    }
}

/// <summary>Office documents and anything-to-PDF via a headless LibreOffice.</summary>
public sealed class LibreOfficeConverter(ToolLocator tools) : IConverter
{
    private static readonly HashSet<string> Inputs =
        ["docx", "doc", "odt", "rtf", "txt", "html", "htm", "xlsx", "xls", "ods", "pptx", "ppt", "odp"];

    private static readonly HashSet<string> TextDocuments = ["docx", "doc", "odt", "rtf"];

    /// <summary>
    /// Profiles not in use. LibreOffice allows one process per profile: a second one (a parallel job) hands its file
    /// to the first and exits without converting. So each concurrent job borrows a profile of its own, kept for the
    /// session since creating one costs a few seconds.
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentBag<int> FreeProfiles = [];
    private static int _profiles;

    public string Name => "LibreOffice";
    public ExternalTool? RequiredTool => ExternalTool.LibreOffice;

    public bool Supports(string sourceExtension, string targetExtension) =>
        Inputs.Contains(sourceExtension)
        && (targetExtension == "pdf"
            || (TextDocuments.Contains(sourceExtension) && targetExtension is "docx" or "odt" or "rtf" && sourceExtension != targetExtension));

    public async Task ConvertAsync(ConversionRequest request, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var soffice = tools.Find(ExternalTool.LibreOffice) ?? throw new ConversionException("LibreOffice isn't installed.");

        // LibreOffice names the output after the input and refuses to run headless next to an open
        // LibreOffice window, so convert into a scratch folder with a private profile, then move the result.
        var scratch = Directory.CreateTempSubdirectory("lemon-");
        var slot = FreeProfiles.TryTake(out var free) ? free : Interlocked.Increment(ref _profiles);
        // The first keeps the name it always had, so an existing profile is reused.
        var profile = new Uri(Path.Combine(JsonFileStore.DefaultDirectory, slot == 1 ? "libreoffice-profile" : $"libreoffice-profile-{slot}")).AbsoluteUri;
        try
        {
            var result = await ProcessRunner.RunAsync(
                soffice,
                [
                    $"-env:UserInstallation={profile}",
                    "--headless", "--norestore", "--nologo",
                    "--convert-to", request.TargetExtension,
                    "--outdir", scratch.FullName,
                    request.InputPath,
                ],
                cancellationToken: cancellationToken);

            var produced = scratch.EnumerateFiles().FirstOrDefault();
            if (result.ExitCode != 0 || produced is null)
                throw new ConversionException("LibreOffice couldn't convert this file. " + ProcessRunner.Summarize(result.StandardError));

            File.Move(produced.FullName, request.OutputPath, overwrite: true);
            progress?.Report(1);
        }
        finally
        {
            FreeProfiles.Add(slot);
            try { scratch.Delete(recursive: true); } catch (IOException) { }
        }
    }
}
