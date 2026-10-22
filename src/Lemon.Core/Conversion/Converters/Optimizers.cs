using System.Globalization;
using ImageMagick;
using ImageMagick.ImageOptimizers;

namespace Lemon.Core.Conversion.Converters;

/// <summary>
/// Optimize mode for images, in the spirit of Clop: a lossless recompression first, then (unless the strength is
/// Lossless) a lighter lossy encode that is only kept when it still looks like the original and saves noticeably more.
/// </summary>
public sealed class ImageOptimizeConverter : IConverter
{
    /// <param name="MaxDissimilarity">
    /// The most structural dissimilarity (0 = identical) a lossy version may have. Re-encoding real photos as
    /// quality-85 JPEG scores about 0.003-0.008, quality 75 about 0.005-0.015; squeezing a photo into 256 colors
    /// scores about 0.05.
    /// </param>
    private sealed record LossySettings(uint JpegQuality, uint WebPQuality, double MaxDissimilarity);

    private static LossySettings? LossyFor(OptimizeStrength strength) => strength switch
    {
        OptimizeStrength.Lossless => null,
        OptimizeStrength.Smaller => new(JpegQuality: 75, WebPQuality: 70, MaxDissimilarity: 0.025),
        _ => new(JpegQuality: 85, WebPQuality: 80, MaxDissimilarity: 0.01),
    };

    public string Name => "ImageMagick";
    public ExternalTool? RequiredTool => null;

    public bool Supports(string sourceExtension, string targetExtension) =>
        targetExtension == Preset.SameFormat && sourceExtension is "jpg" or "jpeg" or "png" or "gif" or "webp";

    // WebP has no lossless recompression to offer.
    public bool Supports(string sourceExtension, Preset preset) =>
        !(sourceExtension == "webp" && preset.OptimizeStrength == OptimizeStrength.Lossless);

    public Task ConvertAsync(ConversionRequest request, IProgress<double>? progress, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var bytes = File.ReadAllBytes(request.InputPath);
                var lossy = LossyFor(request.Preset.OptimizeStrength);
                var best = request.SourceExtension switch
                {
                    "png" => OptimizePng(bytes, lossy, cancellationToken),
                    "gif" => Lossless(bytes, new GifOptimizer()),
                    "webp" => lossy is null ? null : OptimizeWebP(bytes, lossy),
                    _ => OptimizeJpeg(bytes, lossy, cancellationToken),
                };
                if (best is not null)
                    File.WriteAllBytes(request.OutputPath, best);
            }
            catch (MagickException ex)
            {
                throw new ConversionException(ex.Message);
            }
            progress?.Report(1);
        }, cancellationToken);

    private static byte[] OptimizeJpeg(byte[] bytes, LossySettings? settings, CancellationToken cancellationToken)
    {
        // Progressive with optimized Huffman tables: the same pixels in fewer bytes.
        var optimizer = new JpegOptimizer { OptimalCompression = true, Progressive = true };
        var lossless = Lossless(bytes, optimizer);
        cancellationToken.ThrowIfCancellationRequested();
        if (settings is null)
            return lossless;

        // EXIF orientation is left as a tag (no AutoOrient) so the versions compare pixel for pixel.
        using var original = new MagickImage(bytes);
        // Only files saved at a higher quality than ours are re-encoded; going lower again just stacks up artifacts.
        if (original.Quality <= settings.JpegQuality)
            return lossless;

        using var lossy = original.Clone();
        lossy.Quality = settings.JpegQuality;
        var candidate = Lossless(lossy.ToByteArray(MagickFormat.Jpeg), optimizer);
        return Pick(lossless, LooksSame(original, candidate, settings), bytes.Length)!;
    }

    private static byte[]? OptimizePng(byte[] bytes, LossySettings? settings, CancellationToken cancellationToken)
    {
        // ImageMagick would keep only the first frame of an animated PNG, and the bundled 8-bit build can't
        // round-trip 16 bits per channel.
        if (IsAnimatedPng(bytes) || IsDeepPng(bytes))
            return null;

        var optimizer = new PngOptimizer { OptimalCompression = true };
        var lossless = Lossless(bytes, optimizer);
        cancellationToken.ThrowIfCancellationRequested();
        if (settings is null)
            return lossless;

        using var original = new MagickImage(bytes);
        if (original.TotalColors <= 256)
            return lossless;

        // Like pngquant: a 256-color palette (alpha included) is often indistinguishable for screenshots and graphics.
        // No dithering, since the noise it adds costs more bytes than it saves on flat areas.
        using var reduced = original.Clone();
        reduced.Quantize(new QuantizeSettings { Colors = 256, DitherMethod = DitherMethod.No });
        var candidate = Lossless(reduced.ToByteArray(MagickFormat.Png), optimizer);
        cancellationToken.ThrowIfCancellationRequested();
        return Pick(lossless, LooksSame(original, candidate, settings), bytes.Length);
    }

    private static byte[]? OptimizeWebP(byte[] bytes, LossySettings settings)
    {
        using var frames = new MagickImageCollection(bytes);
        // MagickImage would flatten an animation to its first frame; leave animations as they are.
        if (frames.Count != 1)
            return null;

        var original = frames[0];
        using var lossy = original.Clone();
        lossy.Quality = settings.WebPQuality;
        return Pick(null, LooksSame(original, lossy.ToByteArray(MagickFormat.WebP), settings), bytes.Length);
    }

    /// <summary>The lossy version when it saves enough over the lossless one (or the original), else the lossless one.</summary>
    private static byte[]? Pick(byte[]? lossless, byte[]? lossy, long originalLength)
    {
        var baseline = lossless?.Length ?? originalLength;
        return lossy is not null && lossy.Length <= baseline * (1 - OptimizedOutput.MinLossySaving) ? lossy : lossless;
    }

    /// <summary>The candidate if it's close enough to the original for these settings, otherwise null.</summary>
    private static byte[]? LooksSame(IMagickImage<byte> original, byte[] candidate, LossySettings settings)
    {
        using var decoded = new MagickImage(candidate);
        if (decoded.Width != original.Width || decoded.Height != original.Height)
            return null;
        return Dissimilarity(original, decoded) <= settings.MaxDissimilarity ? candidate : null;
    }

    /// <summary>
    /// Structural dissimilarity, measured in horizontal bands on all cores. The bundled ImageMagick compares on a
    /// single thread (about 8 s for a 4K photo); SSIM works on small windows, so the bands give the same score.
    /// </summary>
    private static double Dissimilarity(IMagickImage<byte> original, IMagickImage<byte> candidate)
    {
        const uint bandHeight = 256;
        var bands = new List<(IMagickImage<byte> Original, IMagickImage<byte> Candidate, uint Height)>();
        try
        {
            for (uint y = 0; y < original.Height;)
            {
                // The last band takes the remainder, so no band is too thin for SSIM's window.
                var rows = original.Height - y < 2 * bandHeight ? original.Height - y : bandHeight;
                var area = new MagickGeometry(0, (int)y, original.Width, rows);
                bands.Add((original.CloneArea(area), candidate.CloneArea(area), rows));
                y += rows;
            }
            var weighted = new double[bands.Count];
            Parallel.For(0, bands.Count, i =>
                weighted[i] = bands[i].Original.Compare(bands[i].Candidate, ErrorMetric.StructuralDissimilarity) * bands[i].Height);
            return weighted.Sum() / original.Height;
        }
        finally
        {
            foreach (var band in bands)
            {
                band.Original.Dispose();
                band.Candidate.Dispose();
            }
        }
    }

    private static byte[] Lossless(byte[] bytes, IImageOptimizer optimizer)
    {
        using var stream = new MemoryStream();
        stream.Write(bytes);
        stream.Position = 0;
        // Rewrites the stream only when it found something smaller.
        optimizer.LosslessCompress(stream);
        return stream.ToArray();
    }

    /// <summary>16 bits per channel, from the bit depth byte of the IHDR chunk that always comes first.</summary>
    public static bool IsDeepPng(ReadOnlySpan<byte> bytes) => bytes.Length > 24 && bytes[24] == 16;

    /// <summary>APNG files carry an "acTL" chunk before the first image data.</summary>
    public static bool IsAnimatedPng(ReadOnlySpan<byte> bytes)
    {
        var animation = bytes.IndexOf("acTL"u8);
        var data = bytes.IndexOf("IDAT"u8);
        return animation >= 0 && (data < 0 || animation < data);
    }
}

/// <summary>
/// Optimize mode for video: re-encodes the picture as H.264 (CRF 23 for Balanced, which looks the same; CRF 28 for
/// Smaller) and copies the audio untouched. There's no lossless option: any re-encode changes pixels. Videos that are already efficient (HEVC, AV1, VP9), HDR or more than 8-bit 4:2:0 are left alone,
/// because H.264 would either not beat them or quietly lose color.
/// </summary>
public sealed class VideoOptimizeConverter(ToolLocator tools) : IConverter
{
    private static readonly HashSet<string> Containers = ["mp4", "m4v", "mov", "mkv"];
    private static readonly HashSet<string> EfficientCodecs = ["hevc", "av1", "vp9"];

    public string Name => "FFmpeg";
    public ExternalTool? RequiredTool => ExternalTool.Ffmpeg;

    public bool Supports(string sourceExtension, string targetExtension) =>
        targetExtension == Preset.SameFormat && Containers.Contains(sourceExtension);

    public bool Supports(string sourceExtension, Preset preset) => preset.OptimizeStrength != OptimizeStrength.Lossless;

    public async Task ConvertAsync(ConversionRequest request, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var ffmpeg = tools.Find(ExternalTool.Ffmpeg) ?? throw new ConversionException("FFmpeg isn't installed.");
        var ffprobe = tools.Find(ExternalTool.Ffprobe) ?? throw new ConversionException("Optimizing video needs FFprobe, which comes with FFmpeg.");

        var info = await ProbeAsync(ffprobe, request.InputPath, cancellationToken);
        if (!IsWorthReencoding(info))
            return;

        var result = await ProcessRunner.RunAsync(
            ffmpeg,
            BuildArguments(request),
            line =>
            {
                if (info.Duration is > 0 && FfmpegConverter.TryParseProgress(line, out var seconds))
                    progress?.Report(Math.Clamp(seconds / info.Duration.Value, 0, 1));
            },
            cancellationToken);

        if (result.ExitCode != 0)
            throw new ConversionException(ProcessRunner.Summarize(result.StandardError));
        OptimizedOutput.DiscardUnlessSmaller(request);
        progress?.Report(1);
    }

    public sealed record VideoInfo(string? Codec, string? PixelFormat, string? Transfer, double? Duration);

    public static bool IsWorthReencoding(VideoInfo info) =>
        info.Codec is { } codec && !EfficientCodecs.Contains(codec)
        && info.PixelFormat is "yuv420p" or "yuvj420p" or "nv12"
        && info.Transfer is not ("smpte2084" or "arib-std-b67");

    public static List<string> BuildArguments(ConversionRequest request)
    {
        var crf = request.Preset.OptimizeStrength == OptimizeStrength.Smaller ? "28" : "23";
        var args = new List<string>
        {
            "-hide_banner", "-nostdin", "-y", "-i", request.InputPath,
            // The main picture, every audio and subtitle track; data tracks such as timecodes are dropped.
            "-map", "0:v:0", "-map", "0:a?", "-map", "0:s?",
            // H.264 in 4:2:0 needs even dimensions; odd ones are scaled to the next even size down.
            "-vf", "scale=trunc(iw/2)*2:trunc(ih/2)*2", "-pix_fmt", "yuv420p",
            "-c:v", "libx264", "-crf", crf, "-preset", "slow",
            "-c:a", "copy", "-c:s", "copy",
        };
        // Same container in and out, so every track can be copied as is. MKV may also carry fonts for its subtitles.
        if (request.TargetExtension == "mkv")
            args.AddRange(["-map", "0:t?"]);
        else
            args.AddRange(["-movflags", "+faststart"]);

        args.AddRange(["-progress", "pipe:1", "-nostats", request.OutputPath]);
        return args;
    }

    private static async Task<VideoInfo> ProbeAsync(string ffprobe, string input, CancellationToken cancellationToken)
    {
        var values = new Dictionary<string, string>();
        var result = await ProcessRunner.RunAsync(
            ffprobe,
            ["-v", "error", "-select_streams", "v:0", "-show_entries", "stream=codec_name,pix_fmt,color_transfer:format=duration",
             "-of", "default=noprint_wrappers=1", input],
            line =>
            {
                if (line.Split('=', 2) is [var key, var value])
                    values.TryAdd(key, value);
            },
            cancellationToken);

        if (result.ExitCode != 0)
            throw new ConversionException("FFprobe couldn't read this video. " + ProcessRunner.Summarize(result.StandardError));
        return new VideoInfo(
            values.GetValueOrDefault("codec_name"),
            values.GetValueOrDefault("pix_fmt"),
            values.GetValueOrDefault("color_transfer"),
            double.TryParse(values.GetValueOrDefault("duration"), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null);
    }
}

/// <summary>
/// Optimize mode for PDF via Ghostscript: fonts are subset and compressed, duplicate images merged, and images well
/// above 200 dpi (150 dpi for Smaller) are downsampled. Text and vector graphics stay sharp. There's no lossless
/// option: Ghostscript rewrites every page.
/// </summary>
public sealed class PdfOptimizeConverter(ToolLocator tools) : IConverter
{
    public string Name => "Ghostscript";
    public ExternalTool? RequiredTool => ExternalTool.Ghostscript;

    public bool Supports(string sourceExtension, string targetExtension) =>
        targetExtension == Preset.SameFormat && sourceExtension == "pdf";

    public bool Supports(string sourceExtension, Preset preset) => preset.OptimizeStrength != OptimizeStrength.Lossless;

    public async Task ConvertAsync(ConversionRequest request, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var gs = tools.Find(ExternalTool.Ghostscript) ?? throw new ConversionException("Ghostscript isn't installed.");
        var result = await ProcessRunner.RunAsync(gs, BuildArguments(request), cancellationToken: cancellationToken);
        if (result.ExitCode != 0)
            throw new ConversionException("Ghostscript couldn't optimize this PDF. " + ProcessRunner.Summarize(result.StandardError));
        OptimizedOutput.DiscardUnlessSmaller(request);
        progress?.Report(1);
    }

    public static List<string> BuildArguments(ConversionRequest request)
    {
        // Balanced: /printer keeps JPEG quality high and only images over 300 dpi are touched.
        // Smaller: /ebook's stronger JPEG, and images over 225 dpi come down to 150.
        var smaller = request.Preset.OptimizeStrength == OptimizeStrength.Smaller;
        var dpi = smaller ? "150" : "200";
        return
        [
            "-q", "-dNOPAUSE", "-dBATCH", "-dSAFER",
            "-sDEVICE=pdfwrite", "-dCompatibilityLevel=1.7",
            smaller ? "-dPDFSETTINGS=/ebook" : "-dPDFSETTINGS=/printer",
            "-dDownsampleColorImages=true", "-dColorImageResolution=" + dpi, "-dColorImageDownsampleThreshold=1.5",
            "-dDownsampleGrayImages=true", "-dGrayImageResolution=" + dpi, "-dGrayImageDownsampleThreshold=1.5",
            "-dDetectDuplicateImages=true", "-dCompressFonts=true", "-dSubsetFonts=true",
            // Ghostscript reads % in the output name as a page-number placeholder.
            "-sOutputFile=" + request.OutputPath.Replace("%", "%%"),
            request.InputPath,
        ];
    }
}

internal static class OptimizedOutput
{
    /// <summary>Re-encoding costs a generation of quality, so it has to save at least this much.</summary>
    public const double MinLossySaving = 0.10;

    /// <summary>Empties the output (meaning "keep the original") unless it's clearly smaller than the input.</summary>
    public static void DiscardUnlessSmaller(ConversionRequest request)
    {
        if (new FileInfo(request.OutputPath).Length > new FileInfo(request.InputPath).Length * (1 - MinLossySaving))
            using (new FileStream(request.OutputPath, FileMode.Truncate)) { }
    }
}
