namespace Lemon.Core.Formats;

public enum MediaKind
{
    Image,
    Audio,
    Video,
    Document,
}

public sealed record FileFormat(string Extension, string DisplayName, MediaKind Kind);

/// <summary>Every file extension Lemon knows about, keyed by lower-case extension without the dot.</summary>
public static class FormatCatalog
{
    private static readonly FileFormat[] All =
    [
        new("png", "PNG", MediaKind.Image),
        new("jpg", "JPEG", MediaKind.Image),
        new("jpeg", "JPEG", MediaKind.Image),
        new("webp", "WebP", MediaKind.Image),
        new("avif", "AVIF", MediaKind.Image),
        new("heic", "HEIC", MediaKind.Image),
        new("heif", "HEIF", MediaKind.Image),
        new("gif", "GIF", MediaKind.Image),
        new("bmp", "BMP", MediaKind.Image),
        new("tif", "TIFF", MediaKind.Image),
        new("tiff", "TIFF", MediaKind.Image),
        new("ico", "Icon", MediaKind.Image),

        new("mp3", "MP3", MediaKind.Audio),
        new("wav", "WAV", MediaKind.Audio),
        new("flac", "FLAC", MediaKind.Audio),
        new("m4a", "AAC (M4A)", MediaKind.Audio),
        new("aac", "AAC", MediaKind.Audio),
        new("ogg", "Ogg Vorbis", MediaKind.Audio),
        new("opus", "Opus", MediaKind.Audio),
        new("wma", "WMA", MediaKind.Audio),
        new("aiff", "AIFF", MediaKind.Audio),

        new("mp4", "MP4", MediaKind.Video),
        new("mkv", "MKV", MediaKind.Video),
        new("webm", "WebM", MediaKind.Video),
        new("mov", "QuickTime", MediaKind.Video),
        new("avi", "AVI", MediaKind.Video),
        new("wmv", "WMV", MediaKind.Video),
        new("m4v", "M4V", MediaKind.Video),
        new("flv", "FLV", MediaKind.Video),

        new("pdf", "PDF", MediaKind.Document),
        new("docx", "Word", MediaKind.Document),
        new("doc", "Word 97-2003", MediaKind.Document),
        new("odt", "OpenDocument Text", MediaKind.Document),
        new("rtf", "Rich Text", MediaKind.Document),
        new("md", "Markdown", MediaKind.Document),
        new("txt", "Plain text", MediaKind.Document),
        new("html", "HTML", MediaKind.Document),
        new("htm", "HTML", MediaKind.Document),
        new("epub", "EPUB", MediaKind.Document),
        new("xlsx", "Excel", MediaKind.Document),
        new("xls", "Excel 97-2003", MediaKind.Document),
        new("ods", "OpenDocument Spreadsheet", MediaKind.Document),
        new("pptx", "PowerPoint", MediaKind.Document),
        new("ppt", "PowerPoint 97-2003", MediaKind.Document),
        new("odp", "OpenDocument Presentation", MediaKind.Document),
    ];

    private static readonly Dictionary<string, FileFormat> ByExtension =
        All.ToDictionary(f => f.Extension, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyCollection<string> Extensions => ByExtension.Keys;

    /// <summary>Normalizes ".JPG", "jpg" and "photo.jpg" style input to "jpg".</summary>
    public static string NormalizeExtension(string extensionOrPath)
    {
        var ext = extensionOrPath.Contains('.') ? Path.GetExtension(extensionOrPath) : extensionOrPath;
        return ext.TrimStart('.').ToLowerInvariant();
    }

    public static FileFormat? Find(string extensionOrPath) =>
        ByExtension.GetValueOrDefault(NormalizeExtension(extensionOrPath));

    public static MediaKind? KindOf(string extensionOrPath) => Find(extensionOrPath)?.Kind;

    public static bool IsSupported(string path) => Find(path) is not null;
}
