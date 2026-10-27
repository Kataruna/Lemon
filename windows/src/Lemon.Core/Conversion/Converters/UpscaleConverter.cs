using ImageMagick;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Lemon.Core.Conversion.Converters;

/// <summary>Enlarges an image 2× or 4× with Real-ESRGAN (ONNX Runtime, on the GPU through DirectML when possible).</summary>
public sealed class UpscaleConverter : IConverter
{
    /// <summary>The networks always enlarge 4×; for 2× each tile's result is halved.</summary>
    private const int NetScale = 4;

    /// <summary>The model sees the picture in tiles of this size, so its memory stays the same whatever the picture's size.</summary>
    private const int Tile = 256;

    /// <summary>Context around each tile that's cropped off its result, so the seams between tiles don't show.</summary>
    private const int Pad = 16;

    /// <summary>Longest side of a result: WebP's limit, and it keeps the result's pixels around 1 GB.</summary>
    public const int MaxSide = 16383;

    private static readonly ImageConverter Images = new();

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(UpscaleModel, bool), Lazy<InferenceSession>> Sessions = new();

    /// <summary>DirectML sessions allow only one Run at a time, and parallel queue jobs share them.</summary>
    private static readonly Lock GpuLock = new();

    /// <summary>GPU failures in a row per model. A lost device recovers, a GPU that can't run the model doesn't.</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<UpscaleModel, int> GpuFailures = new();
    private const int MaxGpuFailures = 3;

    private static InferenceSession Session(UpscaleModel model, bool gpu) =>
        Sessions.GetOrAdd((model, gpu), k => new Lazy<InferenceSession>(
            () => BackgroundRemovalConverter.OpenSession(UpscaleModelInfo.Get(k.Item1).Path, gpu))).Value;

    /// <summary>Lemon sits in the tray for days, so the sessions are let go once no job has used them for this long.</summary>
    private static readonly TimeSpan IdleRelease = TimeSpan.FromSeconds(30);
    private static readonly Lock SessionGate = new();
    private static int _jobsRunning;
    private static Timer? _release;

    private static void ReleaseIdleSessions()
    {
        lock (SessionGate)
        {
            if (_jobsRunning > 0)
                return;
            foreach (var key in Sessions.Keys)
                if (Sessions.TryRemove(key, out var session) && session.IsValueCreated)
                    session.Value.Dispose();
        }
    }

    public string Name => "Real-ESRGAN";
    public ExternalTool? RequiredTool => null;

    // Same format in and out, like Resize. Not ICO (256 px at most) or GIF (animated, and 256 colors).
    public bool Supports(string sourceExtension, string targetExtension) =>
        sourceExtension == targetExtension && sourceExtension is not ("pdf" or "ico" or "gif") && Images.Supports(sourceExtension, targetExtension);

    // Only for Upscale presets, and only once the model has been downloaded.
    public bool Supports(string sourceExtension, Preset preset) =>
        preset.Upscale is { } model && UpscaleModelInfo.Get(model).IsInstalled;

    public Task ConvertAsync(ConversionRequest request, IProgress<double>? progress, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (SessionGate)
                _jobsRunning++;
            try
            {
                if (request.SourceExtension == "webp")
                {
                    // Only the first frame would survive.
                    using var frames = new MagickImageCollection();
                    frames.Ping(request.InputPath);
                    if (frames.Count > 1)
                        throw new ConversionException("Can't upscale animated pictures.");
                }

                using var image = new MagickImage(request.InputPath);
                image.AutoOrient();
                // The model sees RGB. A CMYK picture would hand it C, M and Y as colors and leave K to the quick
                // Scale below (blocky), so it goes through sRGB and back.
                var cmyk = image.ColorSpace == ColorSpace.CMYK;
                if (cmyk)
                    image.ColorSpace = ColorSpace.sRGB;
                var scale = request.Preset.Scale ?? NetScale;
                var (width, height) = ((int)image.Width, (int)image.Height);
                if (Math.Max(width, height) * scale > MaxSide)
                    throw new ConversionException($"Too big to upscale {scale}×: the result would be over {MaxSide} px on its long side.");
                progress?.Report(0.05);

                byte[] source;
                using (var pixels = image.GetPixels())
                    source = pixels.ToByteArray(PixelMapping.RGB)!;

                var model = request.Preset.Upscale!.Value;
                byte[]? result = null;
                if (GpuFailures.GetValueOrDefault(model) < MaxGpuFailures)
                {
                    lock (GpuLock)
                    {
                        try
                        {
                            result = Upscale(model, true, source, width, height, scale, progress, cancellationToken);
                            GpuFailures[model] = 0;
                        }
                        catch (OnnxRuntimeException)
                        {
                            // As in BackgroundRemovalConverter: drop the dead session and finish on the CPU.
                            GpuFailures.AddOrUpdate(model, 1, (_, n) => n + 1);
                            if (Sessions.TryRemove((model, true), out var dead) && dead.IsValueCreated)
                                dead.Value.Dispose();
                        }
                    }
                }
                result ??= Upscale(model, false, source, width, height, scale, progress, cancellationToken);

                // Enlarging the original carries its color profile and metadata over; the model's pixels then replace
                // the colors, so a quick Scale does (Lanczos took 0.6 s on a 10 MP result, Sample 1.1 s, Scale 0.25 s).
                // Only transparency needs a smooth enlargement of its own.
                var (outWidth, outHeight) = ((uint)(width * scale), (uint)(height * scale));
                var size = new MagickGeometry(outWidth, outHeight) { IgnoreAspectRatio = true };
                using var alpha = image.HasAlpha ? image.Separate(Channels.Alpha).First() : null;
                if (alpha is not null)
                {
                    alpha.FilterType = FilterType.Lanczos;
                    alpha.Resize(size);
                }
                image.Scale(size);
                image.ImportPixels(result, new PixelImportSettings(outWidth, outHeight, StorageType.Char, PixelMapping.RGB));
                if (alpha is not null)
                    image.Composite(alpha, CompositeOperator.CopyAlpha);
                if (cmyk)
                    image.ColorSpace = ColorSpace.CMYK;
                if (request.Preset.StripMetadata)
                    image.Strip();
                image.Write(request.OutputPath);
            }
            catch (MagickException ex)
            {
                throw new ConversionException(ex.Message);
            }
            catch (OnnxRuntimeException ex)
            {
                throw new ConversionException("Upscaling failed: " + ex.Message);
            }
            finally
            {
                lock (SessionGate)
                {
                    if (--_jobsRunning == 0)
                        (_release ??= new Timer(_ => ReleaseIdleSessions())).Change(IdleRelease, Timeout.InfiniteTimeSpan);
                }
            }
            progress?.Report(1);
        }, cancellationToken);

    /// <summary>Runs the model tile by tile over interleaved RGB bytes and returns the enlarged RGB bytes.</summary>
    private static byte[] Upscale(UpscaleModel model, bool gpu, byte[] source, int width, int height, int scale,
        IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var session = Session(model, gpu);
        var inputName = session.InputMetadata.Keys.First();

        // Every tile has the same shape (edge tiles are moved inward), so DirectML doesn't rebuild its graph per tile.
        var (tw, th) = (Math.Min(Tile + 2 * Pad, width), Math.Min(Tile + 2 * Pad, height));
        var outWidth = width * scale;
        var result = new byte[checked(outWidth * height * scale * 3)];
        var buffer = new float[3 * tw * th];
        var (columns, rows) = ((width + Tile - 1) / Tile, (height + Tile - 1) / Tile);

        for (var row = 0; row < rows; row++)
        for (var column = 0; column < columns; column++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (x0, y0) = (column * Tile, row * Tile);
            var (x1, y1) = (Math.Min(x0 + Tile, width), Math.Min(y0 + Tile, height));
            var (wx, wy) = (Math.Clamp(x0 - Pad, 0, width - tw), Math.Clamp(y0 - Pad, 0, height - th));

            // Planar CHW, 0..1 (Real-ESRGAN takes no mean/std normalization).
            for (var c = 0; c < 3; c++)
                for (var y = 0; y < th; y++)
                    for (var x = 0; x < tw; x++)
                        buffer[(c * th + y) * tw + x] = source[((wy + y) * width + wx + x) * 3 + c] / 255f;

            var (nw, nh) = (tw * NetScale, th * NetScale);
            var tileWidth = tw * scale;
            var tile = new byte[tileWidth * th * scale * 3];
            using (var results = session.Run([NamedOnnxValue.CreateFromTensor(inputName, new DenseTensor<float>(buffer, [1, 3, th, tw]))]))
            {
                // Planar floats to interleaved bytes, a row per task: on one core this took as long as the model.
                // For 2× each 2×2 block is averaged, an exact halving (a Lanczos resize per tile was slower than the model).
                var output = results.First().AsTensor<float>().ToDenseTensor().Buffer;
                var step = NetScale / scale;
                var weight = 255f / (step * step);
                Parallel.For(0, th * scale, y =>
                {
                    var span = output.Span;
                    for (var c = 0; c < 3; c++)
                        for (int x = 0, o = y * tileWidth * 3 + c; x < tileWidth; x++, o += 3)
                        {
                            var sum = 0f;
                            for (var dy = 0; dy < step; dy++)
                                for (var dx = 0; dx < step; dx++)
                                    sum += Math.Clamp(span[(c * nh + y * step + dy) * nw + x * step + dx], 0f, 1f);
                            tile[o] = (byte)(sum * weight + 0.5f);
                        }
                });
            }

            // Keep only the tile's own area; the padding around it was context.
            var length = (x1 - x0) * scale * 3;
            for (var y = (y0 - wy) * scale; y < (y1 - wy) * scale; y++)
                Array.Copy(tile, (y * tileWidth + (x0 - wx) * scale) * 3, result, ((wy * scale + y) * outWidth + x0 * scale) * 3, length);

            progress?.Report(0.05 + 0.9 * (row * columns + column + 1) / (rows * columns));
        }
        return result;
    }
}
