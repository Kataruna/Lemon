using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Lemon.App.ViewModels;

public enum JobStatus
{
    Ready,
    Queued,
    Converting,
    Done,
    Failed,
    Canceled,
}

/// <summary>One file in the queue, its chosen preset and how its conversion is going.</summary>
public sealed partial class JobViewModel : ObservableObject
{
    private readonly long _inputBytes;
    private long _outputBytes;
    private CancellationTokenSource? _cts;

    public JobViewModel(string inputPath, IReadOnlyList<Preset> presets, Preset? preset)
    {
        InputPath = inputPath;
        Kind = FormatCatalog.KindOf(inputPath) ?? MediaKind.Document;
        AvailablePresets = presets;
        SelectedPreset = preset;
        _inputBytes = File.Exists(inputPath) ? new FileInfo(inputPath).Length : 0;
        Details = $"{FormatCatalog.NormalizeExtension(inputPath).ToUpperInvariant()} · {FormatSize(_inputBytes)}";
    }

    public string InputPath { get; }
    public string FileName => Path.GetFileName(InputPath);
    public string Details { get; }
    public MediaKind Kind { get; }

    public string KindGlyph => Kind switch
    {
        MediaKind.Image => "",
        MediaKind.Audio => "",
        MediaKind.Video => "",
        _ => "",
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    public partial IReadOnlyList<Preset> AvailablePresets { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(IsStartable))]
    public partial Preset? SelectedPreset { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditable), nameof(IsActive), nameof(IsDone), nameof(IsFailed), nameof(CanRetry),
        nameof(ShowProgress), nameof(StatusText), nameof(IsStartable))]
    public partial JobStatus Status { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIndeterminate))]
    public partial double Progress { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    public partial string? OutputPath { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    public partial string? Error { get; set; }

    public bool IsEditable => Status is JobStatus.Ready or JobStatus.Failed or JobStatus.Canceled;
    public bool IsStartable => Status == JobStatus.Ready && SelectedPreset is not null;
    public bool IsActive => Status is JobStatus.Queued or JobStatus.Converting;
    public bool IsDone => Status == JobStatus.Done;
    public bool IsFailed => Status == JobStatus.Failed;
    public bool CanRetry => Status is JobStatus.Failed or JobStatus.Canceled;
    public bool ShowProgress => Status == JobStatus.Converting;

    /// <summary>Converters that only report completion get a moving bar instead of one stuck at 0%.</summary>
    public bool IsIndeterminate => Progress <= 0;

    public string StatusText => Status switch
    {
        JobStatus.Ready when SelectedPreset is null => "No converter available",
        JobStatus.Queued => "Waiting…",
        // Optimize hands back the input itself when it couldn't find a worthwhile smaller version.
        JobStatus.Done when OutputPath == InputPath => "Already optimized",
        JobStatus.Done when SelectedPreset is { IsOptimize: true } && _inputBytes > 0 =>
            $"Saved {1 - (double)_outputBytes / _inputBytes:0%} · {FormatSize(_inputBytes)} → {FormatSize(_outputBytes)}",
        JobStatus.Done => $"Saved as {Path.GetFileName(OutputPath)}",
        JobStatus.Failed => Error ?? "Failed",
        JobStatus.Canceled => "Canceled",
        _ => "",
    };

    internal void UpdatePresets(IReadOnlyList<Preset> presets, Preset? fallback)
    {
        var keep = presets.FirstOrDefault(p => p.Id == SelectedPreset?.Id);
        AvailablePresets = presets;
        SelectedPreset = keep ?? fallback;
    }

    internal async Task RunAsync(ConversionService conversion, string? outputFolder)
    {
        if (SelectedPreset is not { } preset)
            return;

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        Error = null;
        Progress = 0;
        Status = JobStatus.Converting;

        // Progress<T> captures the UI thread here, so reports from the worker land back on it.
        var progress = new Progress<double>(p =>
        {
            if (Status == JobStatus.Converting)
                Progress = p;
        });

        try
        {
            var output = await Task.Run(() => conversion.ConvertAsync(InputPath, preset, outputFolder, progress, token), token);
            _outputBytes = new FileInfo(output).Length;
            OutputPath = output;
            Progress = 1;
            Status = JobStatus.Done;
        }
        catch (OperationCanceledException)
        {
            Status = JobStatus.Canceled;
        }
        catch (Exception ex)
        {
            // Any failure (bad file, permissions, tool crash) is reported on the row rather than taking down the app.
            Error = ex.Message;
            Status = JobStatus.Failed;
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        if (Status == JobStatus.Queued)
            Status = JobStatus.Canceled;
        else
            _cts?.Cancel();
    }

    [RelayCommand]
    private void OpenOutput()
    {
        if (OutputPath is not null && File.Exists(OutputPath))
            Process.Start(new ProcessStartInfo(OutputPath) { UseShellExecute = true });
    }

    [RelayCommand]
    private void ShowInFolder()
    {
        if (OutputPath is not null)
            Process.Start("explorer.exe", $"/select,\"{OutputPath}\"");
    }

    /// <summary>ListView uses this as the row's accessible name.</summary>
    public override string ToString() => FileName;

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):0.0} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):0.00} GB",
    };
}
