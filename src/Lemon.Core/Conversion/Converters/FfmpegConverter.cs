using System.Globalization;

namespace Lemon.Core.Conversion.Converters;

/// <summary>Audio and video conversion (and video to animated GIF) by driving ffmpeg.</summary>
public sealed class FfmpegConverter(ToolLocator tools) : IConverter
{
    private static readonly HashSet<string> AudioIn = ["mp3", "wav", "flac", "m4a", "aac", "ogg", "opus", "wma", "aiff"];
    private static readonly HashSet<string> VideoIn = ["mp4", "mkv", "webm", "mov", "avi", "wmv", "m4v", "flv"];
    private static readonly HashSet<string> AudioOut = ["mp3", "wav", "flac", "m4a", "ogg", "opus", "aiff"];
    private static readonly HashSet<string> VideoOut = ["mp4", "mkv", "webm", "mov", "gif"];

    public string Name => "FFmpeg";
    public ExternalTool? RequiredTool => ExternalTool.Ffmpeg;

    public bool Supports(string sourceExtension, string targetExtension)
    {
        // Animated GIFs can become video; GIF to GIF is left to the image converter.
        if (sourceExtension == "gif")
            return targetExtension != "gif" && VideoOut.Contains(targetExtension);
        if (AudioIn.Contains(sourceExtension))
            return AudioOut.Contains(targetExtension);
        return VideoIn.Contains(sourceExtension) && (VideoOut.Contains(targetExtension) || AudioOut.Contains(targetExtension));
    }

    public async Task ConvertAsync(ConversionRequest request, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var ffmpeg = tools.Find(ExternalTool.Ffmpeg) ?? throw new ConversionException("FFmpeg isn't installed.");
        var duration = await ProbeDurationAsync(request.InputPath, cancellationToken);

        var result = await ProcessRunner.RunAsync(
            ffmpeg,
            BuildArguments(request),
            line =>
            {
                if (duration is > 0 && TryParseProgress(line, out var seconds))
                    progress?.Report(Math.Clamp(seconds / duration.Value, 0, 1));
            },
            cancellationToken);

        if (result.ExitCode != 0)
            throw new ConversionException(ProcessRunner.Summarize(result.StandardError));
        progress?.Report(1);
    }

    public static List<string> BuildArguments(ConversionRequest request)
    {
        var preset = request.Preset;
        var target = request.TargetExtension;
        // -sn/-dn: drop subtitle and data streams; image-based subtitles (common in MKV) can't go into MP4 and fail the job.
        var args = new List<string> { "-hide_banner", "-nostdin", "-y", "-i", request.InputPath, "-sn", "-dn" };

        if (AudioOut.Contains(target))
        {
            args.Add("-vn");
            args.AddRange(AudioCodec(target, preset.AudioBitrateKbps));
        }
        else if (target == "gif")
        {
            var width = preset.MaxWidth is { } w ? $"'min(iw,{w})'" : "iw";
            args.AddRange([
                "-vf", $"fps={preset.Fps ?? 12},scale=w={width}:h=-1:flags=lanczos,split[s0][s1];[s0]palettegen[p];[s1][p]paletteuse",
                "-an", "-loop", "0",
            ]);
        }
        else
        {
            // x264 and VP9 with yuv420p need even dimensions, so always scale, even without a size limit.
            var w = preset.MaxWidth is { } mw ? $"'min(iw,{mw})'" : "iw";
            var h = preset.MaxHeight is { } mh ? $"'min(ih,{mh})'" : "ih";
            args.AddRange(["-vf", $"scale=w={w}:h={h}:force_original_aspect_ratio=decrease:force_divisible_by=2", "-pix_fmt", "yuv420p"]);
            if (preset.Fps is { } fps)
                args.AddRange(["-r", fps.ToString(CultureInfo.InvariantCulture)]);

            var crf = (preset.VideoCrf ?? (target == "webm" ? 32 : 23)).ToString(CultureInfo.InvariantCulture);
            if (target == "webm")
            {
                args.AddRange(["-c:v", "libvpx-vp9", "-crf", crf, "-b:v", "0", "-row-mt", "1", "-deadline", "good", "-cpu-used", "4"]);
                args.AddRange(AudioCodec("opus", preset.AudioBitrateKbps ?? 128));
            }
            else
            {
                args.AddRange(["-c:v", "libx264", "-crf", crf, "-preset", "medium"]);
                args.AddRange(AudioCodec("m4a", preset.AudioBitrateKbps ?? 160));
                if (target is "mp4" or "mov")
                    args.AddRange(["-movflags", "+faststart"]);
            }
        }

        if (preset.StripMetadata)
            args.AddRange(["-map_metadata", "-1"]);

        args.AddRange(["-progress", "pipe:1", "-nostats", request.OutputPath]);
        return args;
    }

    private static IEnumerable<string> AudioCodec(string target, int? bitrateKbps)
    {
        string Bitrate(int fallback) => $"{bitrateKbps ?? fallback}k";
        return target switch
        {
            "mp3" => ["-c:a", "libmp3lame", "-b:a", Bitrate(192)],
            "m4a" => ["-c:a", "aac", "-b:a", Bitrate(256)],
            "ogg" => ["-c:a", "libvorbis", "-b:a", Bitrate(192)],
            "opus" => ["-c:a", "libopus", "-b:a", Bitrate(128)],
            "flac" => ["-c:a", "flac"],
            "wav" => ["-c:a", "pcm_s16le"],
            "aiff" => ["-c:a", "pcm_s16be"],
            _ => throw new ArgumentOutOfRangeException(nameof(target), target, null),
        };
    }

    /// <summary>Parses ffmpeg's "-progress" key=value lines, returning the encoded position in seconds.</summary>
    public static bool TryParseProgress(string line, out double seconds)
    {
        seconds = 0;
        // out_time_us is in microseconds (out_time_ms is too, despite its name).
        if (!line.StartsWith("out_time_us=", StringComparison.Ordinal))
            return false;
        if (!long.TryParse(line.AsSpan("out_time_us=".Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out var micros) || micros < 0)
            return false;
        seconds = micros / 1_000_000d;
        return true;
    }

    private async Task<double?> ProbeDurationAsync(string input, CancellationToken cancellationToken)
    {
        if (tools.Find(ExternalTool.Ffprobe) is not { } ffprobe)
            return null;

        string? output = null;
        var result = await ProcessRunner.RunAsync(
            ffprobe,
            ["-v", "error", "-show_entries", "format=duration", "-of", "default=noprint_wrappers=1:nokey=1", input],
            line => output ??= line,
            cancellationToken);

        return result.ExitCode == 0 && double.TryParse(output, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;
    }
}
