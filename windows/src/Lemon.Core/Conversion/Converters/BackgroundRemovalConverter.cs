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

    /// <summary>
    /// How long the last run of each model took on the GPU or CPU, to estimate progress while the next one runs
    /// (ONNX Runtime can't report any). Until a model has run, a rough guess.
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(BackgroundModel, bool), double> RunSeconds = new();

    private static double ExpectedSeconds(BackgroundModel model, bool gpu) =>
        RunSeconds.TryGetValue((model, gpu), out var seconds) ? seconds
        : model == BackgroundModel.Lite ? (gpu ? 10 : 15)
        : gpu ? 25 : 45;

    private static InferenceSession Session(BackgroundModel model, bool gpu) =>
        Sessions.GetOrAdd((model, gpu), k => new Lazy<InferenceSession>(() => OpenSession(BackgroundModelInfo.Get(k.Item1).Path, gpu))).Value;

    /// <summary>
    /// A loaded model plus its working memory is several GB, and Lemon sits in the tray for days, so the sessions are
    /// let go once no job has used them for this long. Reloading takes a second or two.
    /// </summary>
    private static readonly TimeSpan IdleRelease = TimeSpan.FromSeconds(30);
    private static readonly Lock SessionGate = new();
    private static int _jobsRunning;
    private static Timer? _release;

    private static void ReleaseIdleSessions()
    {
        lock (SessionGate)
        {
            // A job may have started since the timer fired; it needs its session.
            if (_jobsRunning > 0)
                return;
            foreach (var key in Sessions.Keys)
                if (Sessions.TryRemove(key, out var session) && session.IsValueCreated)
                    session.Value.Dispose();
        }
    }

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
            lock (SessionGate)
                _jobsRunning++;
            try
            {
                using var image = new MagickImage(request.InputPath);
                image.AutoOrient();
                // The model sees RGB: a CMYK picture would hand it C, M and Y as colors, and it would miss the subject.
                if (image.ColorSpace == ColorSpace.CMYK)
                    image.ColorSpace = ColorSpace.sRGB;
                progress?.Report(0.1);
                var model = request.Preset.RemoveBackground!.Value;
                MagickImage? mask = null;
                if (GpuFailures.GetValueOrDefault(model) < MaxGpuFailures)
                {
                    lock (GpuLock)
                    {
                        try
                        {
                            mask = Predict(model, true, image, progress);
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
                mask ??= Predict(model, false, image, progress);
                using var maskScope = mask;
                cancellationToken.ThrowIfCancellationRequested();
                mask.Resize(new MagickGeometry(image.Width, image.Height) { IgnoreAspectRatio = true });
                image.Alpha(AlphaOption.Set);
                image.Composite(mask, CompositeOperator.CopyAlpha);
                if (request.Preset.StripMetadata)
                    image.Strip();
                ImageConverter.Save(image, request.OutputPath, MagickFormat.Png);
            }
            catch (MagickException ex)
            {
                throw new ConversionException(ex.Message);
            }
            catch (OnnxRuntimeException ex)
            {
                throw new ConversionException("Background removal failed: " + ex.Message);
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

    /// <summary>Opens a model on the GPU through DirectML, or on the CPU when asked or when DirectML isn't there.</summary>
    internal static InferenceSession OpenSession(string path, bool useGpu)
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
    private static MagickImage Predict(BackgroundModel model, bool gpu, MagickImage source, IProgress<double>? progress)
    {
        var session = Session(model, gpu);

        // ONNX Runtime can't report progress, so estimate it from how long the last run took (shrinking the image
        // included): it rises quickly at first, then slows as it nears 90%, so a run that takes longer than last
        // time still looks alive instead of sitting full.
        var expected = ExpectedSeconds(model, gpu);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var timer = new Timer(_ => progress?.Report(0.1 + 0.8 * (1 - Math.Exp(-2 * watch.Elapsed.TotalSeconds / expected))), null, 0, 200);
        IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results;
        try
        {
            // Planar CHW straight into the tensor's buffer (the 4-D indexer costs a bounds check and stride sum per value).
            var buffer = new float[3 * Size * Size];
            using (var small = new MagickImage(source))
            {
                small.Alpha(AlphaOption.Remove);
                small.Resize(new MagickGeometry(Size, Size) { IgnoreAspectRatio = true });
                using var pixels = small.GetPixels();
                var data = pixels.ToByteArray(PixelMapping.RGB)!;
                for (var c = 0; c < 3; c++)
                    for (var i = 0; i < Size * Size; i++)
                        buffer[c * Size * Size + i] = (data[i * 3 + c] / 255f - Mean[c]) / Std[c];
            }
            var input = new DenseTensor<float>(buffer, [1, 3, Size, Size]);

            // Without arena shrinkage ONNX Runtime's CPU arena keeps growing between runs (measured on the Mac build with
            // the same model: 6.4 GB after one run, 11 GB after two; with it, steady at about 6.5 GB).
            using var options = new RunOptions();
            options.AddRunConfigEntry("memory.enable_memory_arena_shrinkage", "cpu:0");
            results = session.Run(
                [NamedOnnxValue.CreateFromTensor(session.InputMetadata.Keys.First(), input)], session.OutputMetadata.Keys.ToList(), options);
        }
        finally
        {
            // Wait for a tick in flight, so it can't land after the reports below and move the bar backwards.
            using var stopped = new ManualResetEvent(false);
            if (timer.Dispose(stopped))
                stopped.WaitOne();
        }
        RunSeconds[(model, gpu)] = watch.Elapsed.TotalSeconds;
        progress?.Report(0.9);
        using var resultsScope = results;
        var output = results.First().AsTensor<float>().ToArray();

        // The export returns raw logits; squash them to 0..1 unless it already did.
        var logits = output.Min() < 0 || output.Max() > 1;
        var bytes = output.Select(v => (byte)Math.Round(Math.Clamp(logits ? 1f / (1f + MathF.Exp(-v)) : v, 0f, 1f) * 255)).ToArray();
        return new MagickImage(bytes, new PixelReadSettings(Size, Size, StorageType.Char, "I"));
    }
}
