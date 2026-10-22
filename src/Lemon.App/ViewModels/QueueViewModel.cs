using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Lemon.App.ViewModels;

/// <summary>The batch of files waiting to be converted, and the scheduler that runs them in parallel.</summary>
public sealed partial class QueueViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly PresetStore _presets;
    private readonly ConversionService _conversion;
    private readonly Dictionary<JobViewModel, Task> _running = [];

    public QueueViewModel(AppSettings settings, PresetStore presets, ConversionService conversion)
    {
        _settings = settings;
        _presets = presets;
        _conversion = conversion;
        _presets.Changed += (_, _) => RefreshPresets();
        Jobs.CollectionChanged += OnJobsChanged;
        UpdateSummary();
    }

    public ObservableCollection<JobViewModel> Jobs { get; } = [];

    /// <summary>A conversion ended with an error (not a cancellation).</summary>
    public event Action<JobViewModel>? JobFailed;

    [ObservableProperty]
    public partial bool IsEmpty { get; private set; } = true;

    [ObservableProperty]
    public partial bool IsBusy { get; private set; }

    [ObservableProperty]
    public partial bool HasStartableJobs { get; private set; }

    [ObservableProperty]
    public partial bool HasFinishedJobs { get; private set; }

    [ObservableProperty]
    public partial string Summary { get; private set; } = "";

    /// <summary>One-off message such as "2 files were skipped".</summary>
    [ObservableProperty]
    public partial string? Notice { get; set; }

    /// <summary>Which missing tools would let Lemon handle more of the queued files.</summary>
    [ObservableProperty]
    public partial string? MissingToolsMessage { get; private set; }

    /// <summary>Adds files (and the supported files inside any folders) to the queue.</summary>
    /// <param name="quickTarget">From "Convert with Lemon › To PNG": use a preset for this format and start right away.</param>
    /// <param name="startWith">From the conversion wheel: use this preset where it fits and start right away.</param>
    public void Add(IEnumerable<string> paths, string? quickTarget = null, Preset? startWith = null)
    {
        var skipped = 0;
        var missingTools = new HashSet<ExternalTool>();
        var added = new List<JobViewModel>();
        foreach (var path in ExpandFolders(paths))
        {
            if (!FormatCatalog.IsSupported(path))
            {
                skipped++;
                continue;
            }
            if (Jobs.FirstOrDefault(j => j.IsEditable && string.Equals(j.InputPath, path, StringComparison.OrdinalIgnoreCase)) is { } waiting)
            {
                // Already waiting in the queue. A wheel format or right-click "To PNG" still means "convert it now
                // like this", so reuse that row instead of silently doing nothing.
                if (startWith is not null || quickTarget is not null)
                {
                    waiting.SelectedPreset = waiting.AvailablePresets.FirstOrDefault(p => p.Id == startWith?.Id)
                        ?? DefaultPreset(path, waiting.AvailablePresets, quickTarget);
                    added.Add(waiting);
                }
                continue;
            }

            var presets = PresetsFor(path);
            if (presets.Count == 0)
            {
                skipped++;
                missingTools.UnionWith(_conversion.MissingToolsFor(path));
                continue;
            }
            var preset = presets.FirstOrDefault(p => p.Id == startWith?.Id) ?? DefaultPreset(path, presets, quickTarget);
            var job = new JobViewModel(path, presets, preset);
            job.PropertyChanged += OnJobChanged;
            Jobs.Add(job);
            added.Add(job);
        }

        Notice = skipped switch
        {
            0 => null,
            1 => "1 file was skipped because Lemon can't convert that type of file.",
            _ => $"{skipped} files were skipped because Lemon can't convert those types of files.",
        };
        if (missingTools.Count > 0)
            Notice += " Installing " + string.Join(" or ", missingTools.Select(ToolLocator.DisplayName)) + " would help: "
                      + string.Join("  ·  ", missingTools.Select(ToolLocator.InstallHint));

        if (quickTarget is not null || startWith is not null)
            Enqueue(added);
    }

    /// <summary>Starts every file that's ready.</summary>
    public void Start() => Enqueue(Jobs.Where(j => j.IsStartable).ToList());

    public void Retry(JobViewModel job)
    {
        if (job.CanRetry)
            Enqueue([job]);
    }

    public void CancelAll()
    {
        foreach (var job in Jobs.Where(j => j.IsActive).ToList())
            job.CancelCommand.Execute(null);
    }

    /// <summary>Cancels everything and waits until each job has stopped and removed its partial output.</summary>
    public Task CancelAllAndWaitAsync()
    {
        CancelAll();
        return Task.WhenAll(_running.Values.ToList());
    }

    public void Remove(JobViewModel job)
    {
        if (job.IsActive)
            return;
        job.PropertyChanged -= OnJobChanged;
        Jobs.Remove(job);
    }

    public void ClearFinished()
    {
        foreach (var job in Jobs.Where(j => j.IsDone).ToList())
            Remove(job);
    }

    public void ClearAll()
    {
        foreach (var job in Jobs.Where(j => !j.IsActive).ToList())
            Remove(job);
    }

    /// <summary>The kinds of files in the queue that can still be changed, with presets that fit them.</summary>
    public IEnumerable<(MediaKind Kind, IReadOnlyList<Preset> Presets)> PresetChoicesByKind() =>
        Jobs.Where(j => j.IsEditable)
            .GroupBy(j => j.Kind)
            .OrderBy(g => g.Key)
            .Select(g => (g.Key, (IReadOnlyList<Preset>)g.SelectMany(j => j.AvailablePresets).DistinctBy(p => p.Id).ToList()));

    public void ApplyToAll(MediaKind kind, Preset preset)
    {
        foreach (var job in Jobs.Where(j => j.IsEditable && j.Kind == kind && j.AvailablePresets.Contains(preset)))
            job.SelectedPreset = preset;
    }

    /// <summary>Recomputes which presets fit each file, after presets change or tools are installed.</summary>
    public void RefreshPresets()
    {
        foreach (var job in Jobs.Where(j => j.IsEditable))
        {
            var presets = PresetsFor(job.InputPath);
            job.UpdatePresets(presets, DefaultPreset(job.InputPath, presets, null));
        }
        UpdateSummary();
    }

    private void Enqueue(IReadOnlyList<JobViewModel> jobs)
    {
        foreach (var job in jobs.Where(j => j.SelectedPreset is not null))
            job.Status = JobStatus.Queued;
        Pump();
    }

    /// <summary>Starts queued jobs until the parallel limit is reached. Runs on the UI thread only.</summary>
    private void Pump()
    {
        while (_running.Count < Math.Max(1, _settings.MaxParallelJobs)
               && Jobs.FirstOrDefault(j => j.Status == JobStatus.Queued && !_running.ContainsKey(j)) is { } job)
            _running[job] = RunAsync(job);
        IsBusy = _running.Count > 0;
    }

    private async Task RunAsync(JobViewModel job)
    {
        // Let Pump record this task before it can finish and remove itself.
        await Task.Yield();
        await job.RunAsync(_conversion, _settings.OutputFolder);
        _running.Remove(job);
        Pump();
    }

    /// <summary>The presets that can convert this file, same kind of output first.</summary>
    public IReadOnlyList<Preset> PresetsFor(string path)
    {
        var kind = FormatCatalog.KindOf(path);
        var extension = FormatCatalog.NormalizeExtension(path);
        return _presets.All
            .Where(p => _conversion.CanConvert(path, p))
            // Re-encoding to the same format is only useful when the preset changes something.
            .Where(p => FormatCatalog.NormalizeExtension(p.TargetExtension) != extension || HasOptions(p))
            // Still-image presets (e.g. plain "GIF") make full-size, full-frame-rate GIFs from video; only offer
            // image presets that were made for animation.
            .Where(p => kind != MediaKind.Video || p.TargetKind != MediaKind.Image || p.Fps is not null)
            // ...and the other way round: animation presets mean nothing for a still image.
            .Where(p => kind != MediaKind.Image || p.Fps is null || extension is "gif" or "webp")
            .OrderBy(p => p.TargetKind != kind)
            .ToList();
    }

    private Preset? DefaultPreset(string path, IReadOnlyList<Preset> presets, string? quickTarget)
    {
        var kind = FormatCatalog.KindOf(path) ?? MediaKind.Document;
        var last = _settings.LastPresetByKind.TryGetValue(kind, out var id) ? presets.FirstOrDefault(p => p.Id == id) : null;

        if (quickTarget is not null)
        {
            var matching = presets.Where(p => FormatCatalog.NormalizeExtension(p.TargetExtension) == quickTarget).ToList();
            return matching.FirstOrDefault(p => p.Id == last?.Id) ?? matching.FirstOrDefault(p => p.TargetKind == kind) ?? matching.FirstOrDefault();
        }

        var extension = FormatCatalog.NormalizeExtension(path);
        return last ?? presets.FirstOrDefault(p => FormatCatalog.NormalizeExtension(p.TargetExtension) != extension) ?? presets.FirstOrDefault();
    }

    private static bool HasOptions(Preset p) =>
        p.Quality is not null || p.MaxWidth is not null || p.MaxHeight is not null || p.AudioBitrateKbps is not null
        || p.VideoCrf is not null || p.Fps is not null || p.StripMetadata;

    /// <summary>Replaces folders with the supported files inside them (recursively); files pass through.</summary>
    public static IEnumerable<string> ExpandFolders(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (Directory.Exists(path))
            {
                IEnumerable<string> files;
                try
                {
                    files = Directory.EnumerateFiles(path, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true })
                        .Where(FormatCatalog.IsSupported)
                        .Order(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }
                catch (IOException)
                {
                    continue;
                }
                foreach (var file in files)
                    yield return file;
            }
            else if (File.Exists(path))
            {
                yield return path;
            }
        }
    }

    private void OnJobsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        IsEmpty = Jobs.Count == 0;
        UpdateSummary();
    }

    private void OnJobChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not JobViewModel job)
            return;

        if (e.PropertyName == nameof(JobViewModel.SelectedPreset) && job.SelectedPreset is { } preset && job.IsEditable)
        {
            // Newly added files of the same kind default to whatever the user picked last.
            if (_settings.LastPresetByKind.GetValueOrDefault(job.Kind) != preset.Id)
            {
                _settings.LastPresetByKind[job.Kind] = preset.Id;
                _settings.Save();
            }
        }

        if (e.PropertyName is nameof(JobViewModel.Status) or nameof(JobViewModel.SelectedPreset))
            UpdateSummary();

        if (e.PropertyName == nameof(JobViewModel.Status) && job.IsFailed)
            JobFailed?.Invoke(job);
    }

    private void UpdateSummary()
    {
        HasStartableJobs = Jobs.Any(j => j.IsStartable);
        HasFinishedJobs = Jobs.Any(j => j.IsDone);

        var done = Jobs.Count(j => j.IsDone);
        var failed = Jobs.Count(j => j.IsFailed);
        var parts = new List<string> { Jobs.Count == 1 ? "1 file" : $"{Jobs.Count} files" };
        if (done > 0)
            parts.Add($"{done} converted");
        if (failed > 0)
            parts.Add($"{failed} failed");
        Summary = Jobs.Count == 0 ? "" : string.Join(" · ", parts);

        var missing = Jobs.SelectMany(j => _conversion.MissingToolsFor(j.InputPath)).Distinct().ToList();
        MissingToolsMessage = missing.Count == 0
            ? null
            : "Install " + string.Join(" and ", missing.Select(ToolLocator.DisplayName)) + " to unlock more formats for these files: "
              + string.Join("  ·  ", missing.Select(ToolLocator.InstallHint));
    }
}
