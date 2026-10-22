using ImageMagick;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Lemon.Core.Conversion.Converters;

/// <summary>Cuts out the subject of an image with BiRefNet (ONNX Runtime, on the GPU through DirectML when possible).</summary>
public sealed class BackgroundRemovalConverter : IConverter
{
    /// <summary>Added to results: "photo.jpg" becomes "photo (no background).png".</summary>
    public const string Suffix = " (no background)";

    private const int Size = 1024;
    private static readonly float[] Mean = [0.485f, 0.456f, 0.406f];
    private static readonly float[] Std = [0.229f, 0.224f, 0.225f];

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(BackgroundModel, bool), Lazy<InferenceSession>> Sessions = new();

    /// <summary>
    /// DirectML sessions allow only one Run at a time, and parallel queue jobs share them, so GPU work goes one at a time.
    /// CPU sessions are safe to run concurrently.
    /// </summary>
    private static readonly Lock GpuLock = new();

    /// <summary>GPU failures in a row per model. A lost device recovers, a GPU that can't run the model doesn't.</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<BackgroundModel, int> GpuFailures = new();
    private const int MaxGpuFailures = 3;

    private static InferenceSession Session(BackgroundModel model, bool gpu) =>
        Sessions.GetOrAdd((model, gpu), k => new Lazy<InferenceSession>(() => OpenSession(BackgroundModelInfo.Get(k.Item1).Path, gpu))).Value;

    public string Name => "BiRefNet";
    public ExternalTool? RequiredTool => null;

    public bool Supports(string sourceExtension, string targetExtension) =>
        sourceExtension is not ("pdf" or "ico") && FormatCatalog.KindOf(sourceExtension) == MediaKind.Image && targetExtension == "png";

    // Only for Remove background presets, and only once the model has been downloaded.
    public bool Supports(string sourceExtension, Preset preset) =>
        preset.RemoveBackground is { } model && BackgroundModelInfo.Get(model).IsInstalled;

    public Task ConvertAsync(ConversionRequest request, IProgress<double>? progress, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var image = new MagickImage(request.InputPath);
                image.AutoOrient();
                progress?.Report(0.1);
                var model = request.Preset.RemoveBackground!.Value;
                MagickImage? mask = null;
                if (GpuFailures.GetValueOrDefault(model) < MaxGpuFailures)
                {
                    lock (GpuLock)
                    {
                        try
                        {
                            mask = Predict(Session(model, true), image);
                            GpuFailures[model] = 0;
                        }
                        catch (OnnxRuntimeException)
                        {
                            // GPU lost mid-run (e.g. "device instance has been suspended"): dispose the dead session so
                            // the next file tries the GPU again, and finish this one on the CPU. After a few failures
                            // in a row the GPU can't run this model, so stop rebuilding its session for every file.
                            GpuFailures.AddOrUpdate(model, 1, (_, n) => n + 1);
                            if (Sessions.TryRemove((model, true), out var dead) && dead.IsValueCreated)
                                dead.Value.Dispose();
                        }
                    }
                }
                mask ??= Predict(Session(model, false), image);
                using var maskScope = mask;
                cancellationToken.ThrowIfCancellationRequested();
                mask.Resize(new MagickGeometry(image.Width, image.Height) { IgnoreAspectRatio = true });
                image.Alpha(AlphaOption.Set);
                image.Composite(mask, CompositeOperator.CopyAlpha);
                if (request.Preset.StripMetadata)
                    image.Strip();
                image.Write(request.OutputPath, MagickFormat.Png);
            }
            catch (MagickException ex)
            {
                throw new ConversionException(ex.Message);
            }
            catch (OnnxRuntimeException ex)
            {
                throw new ConversionException("Background removal failed: " + ex.Message);
            }
            progress?.Report(1);
        }, cancellationToken);

    private static InferenceSession OpenSession(string path, bool useGpu)
    {
        try
        {
            if (!useGpu)
                return new InferenceSession(path);
            using var gpu = new SessionOptions();
            gpu.AppendExecutionProvider_DML();
            return new InferenceSession(path, gpu);
        }
        catch (Exception ex) when (ex is OnnxRuntimeException or EntryPointNotFoundException or DllNotFoundException)
        {
            return new InferenceSession(path);
        }
    }

    /// <summary>Runs the model and returns its 1024x1024 foreground mask as a grayscale image.</summary>
    private static MagickImage Predict(InferenceSession session, MagickImage source)
    {
        var input = new DenseTensor<float>([1, 3, Size, Size]);
        using (var small = new MagickImage(source))
        {
            small.Alpha(AlphaOption.Remove);
            small.Resize(new MagickGeometry(Size, Size) { IgnoreAspectRatio = true });
            using var pixels = small.GetPixels();
            var data = pixels.ToByteArray(PixelMapping.RGB)!;
            for (var i = 0; i < Size * Size; i++)
                for (var c = 0; c < 3; c++)
                    input[0, c, i / Size, i % Size] = (data[i * 3 + c] / 255f - Mean[c]) / Std[c];
        }

        using var results = session.Run([NamedOnnxValue.CreateFromTensor(session.InputMetadata.Keys.First(), input)]);
        var output = results.First().AsTensor<float>().ToArray();

        // The export returns raw logits; squash them to 0..1 unless it already did.
        var logits = output.Min() < 0 || output.Max() > 1;
        var bytes = output.Select(v => (byte)Math.Round(Math.Clamp(logits ? 1f / (1f + MathF.Exp(-v)) : v, 0f, 1f) * 255)).ToArray();
        return new MagickImage(bytes, new PixelReadSettings(Size, Size, StorageType.Char, "I"));
    }
}
