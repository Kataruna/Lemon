namespace Lemon.Core.Presets;

public static class BuiltInPresets
{
    /// <summary>The Balanced Optimize preset, used wherever Optimize is picked without choosing a strength.</summary>
    public const string OptimizeId = "optimize";

    public static IReadOnlyList<Preset> All { get; } =
    [
        Make("optimize-lossless", "Optimize – Lossless", Preset.SameFormat, p => p with { OptimizeStrength = OptimizeStrength.Lossless }),
        Make(OptimizeId, "Optimize – Balanced", Preset.SameFormat),
        Make("optimize-smaller", "Optimize – Smaller", Preset.SameFormat, p => p with { OptimizeStrength = OptimizeStrength.Smaller }),

        Make("img-nobg", "Remove background – Lite", "png", p => p with { RemoveBackground = BackgroundModel.Lite }),
        Make("img-nobg-full", "Remove background – Full quality", "png", p => p with { RemoveBackground = BackgroundModel.Full }),

        Make("img-png", "PNG", "png"),
        Make("img-jpg", "JPEG – High quality", "jpg", p => p with { Quality = 90 }),
        Make("img-webp", "WebP – Web optimized", "webp", p => p with { Quality = 80, MaxWidth = 1920, MaxHeight = 1920, StripMetadata = true }),
        Make("img-avif", "AVIF – Small", "avif", p => p with { Quality = 60 }),
        Make("img-gif", "GIF", "gif"),
        Make("img-tiff", "TIFF", "tiff"),
        Make("img-bmp", "BMP", "bmp"),
        Make("img-ico", "Windows icon (256 px)", "ico", p => p with { MaxWidth = 256, MaxHeight = 256 }),

        Make("aud-mp3-320", "MP3 – 320 kbps", "mp3", p => p with { AudioBitrateKbps = 320 }),
        Make("aud-mp3-192", "MP3 – 192 kbps", "mp3", p => p with { AudioBitrateKbps = 192 }),
        Make("aud-m4a", "AAC (M4A) – 256 kbps", "m4a", p => p with { AudioBitrateKbps = 256 }),
        Make("aud-opus", "Opus – 128 kbps", "opus", p => p with { AudioBitrateKbps = 128 }),
        Make("aud-ogg", "Ogg Vorbis – 192 kbps", "ogg", p => p with { AudioBitrateKbps = 192 }),
        Make("aud-flac", "FLAC – Lossless", "flac"),
        Make("aud-wav", "WAV – Uncompressed", "wav"),
        Make("aud-aiff", "AIFF – Uncompressed", "aiff"),

        Make("vid-mp4", "MP4 (H.264)", "mp4", p => p with { VideoCrf = 23 }),
        Make("vid-mp4-1080", "MP4 – 1080p", "mp4", p => p with { VideoCrf = 23, MaxHeight = 1080 }),
        Make("vid-mp4-720", "MP4 – 720p small", "mp4", p => p with { VideoCrf = 27, MaxHeight = 720 }),
        Make("vid-webm", "WebM (VP9)", "webm", p => p with { VideoCrf = 32 }),
        Make("vid-mkv", "MKV (H.264)", "mkv", p => p with { VideoCrf = 21 }),
        Make("vid-mov", "QuickTime MOV", "mov", p => p with { VideoCrf = 21 }),
        Make("vid-gif", "Animated GIF", "gif", p => p with { Fps = 12, MaxWidth = 480 }),

        Make("doc-pdf", "PDF", "pdf"),
        Make("doc-docx", "Word (DOCX)", "docx"),
        Make("doc-odt", "OpenDocument (ODT)", "odt"),
        Make("doc-rtf", "Rich Text (RTF)", "rtf"),
        Make("doc-html", "HTML page", "html"),
        Make("doc-md", "Markdown", "md"),
        Make("doc-txt", "Plain text", "txt"),
        Make("doc-epub", "EPUB e-book", "epub"),
    ];

    private static Preset Make(string id, string name, string ext, Func<Preset, Preset>? configure = null)
    {
        var preset = new Preset { Id = id, Name = name, TargetExtension = ext, IsBuiltIn = true };
        return configure is null ? preset : configure(preset);
    }
}
