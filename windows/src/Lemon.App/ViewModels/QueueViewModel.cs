using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;

namespace Lemon.App.ViewModels;

/// <summary>The batch of files waiting to be converted, and the scheduler that runs them in parallel.</summary>
public sealed partial class QueueViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly PresetStore _presets;
    private readonly ConversionService _conversion;
    private readonly Dictionary<JobViewModel, Task> _running = [];
    /// <summary>Jobs started since the queue was last idle, for the overall progress and the "done" notification.</summary>
    private readonly HashSet<JobViewModel> _batch = [];
    /// <summary>
    /// Set while many jobs change at once (a folder added, a batch queued): the summary and progress are worked out
    /// once at the end instead of after every file, which made dropping thousands of files take minutes.
    /// </summary>
    private bool _bulk;
    /// <summary>Works out the summary and the batch's progress at most every 100 ms while jobs change; see <see cref="OnJobChanged"/>.</summary>
    private readonly DispatcherQueueTimer _refresh;
    private bool _summaryDue;

    public QueueViewModel(AppSettings settings, PresetStore presets, ConversionService conversion)
    {
        _settings = settings;
        _presets = presets;
        _conversion = conversion;
        _refresh = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _refresh.Interval = TimeSpan.FromMilliseconds(100);
        _refresh.IsRepeating = false;
        _refresh.Tick += (_, _) =>
        {
            if (_summaryDue)
                UpdateSummary();
            _summaryDue = false;
            if (_batch.Count > 0)
                UpdateBatchProgress();
        };
        _presets.Changed += (_, _) => RefreshPresets();
        Jobs.CollectionChanged += OnJobsChanged;
        UpdateSummary();
    }

    public ObservableCollection<JobViewModel> Jobs { get; } = [];

    /// <summary>Everything that was started has finished; the jobs of that batch, done, failed or canceled.</summary>
    public event Action<IReadOnlyList<JobViewModel>>? BatchFinished;

    /// <summary>How far along the running batch is, 0-1, or null when nothing is converting.</summary>
    [ObservableProperty]
    public partial double? BatchProgress { get; private set; }

    /// <summary>e.g. "Removing background · 2 of 5 done · 40%", while a batch runs.</summary>
    [ObservableProperty]
    public partial string BatchStatus { get; private set; } = "";

    [ObservableProperty]
    public partial bool IsEmpty { get; private set; } = true;

    [ObservableProperty]
    public partial bool IsBusy { get; private set; }

    [ObservableProperty]
    public partial bool HasStartableJobs { get; private set; }

    [ObservableProperty]
    public partial bool HasFinishedJobs { get; private set; }

    [ObservableProperty]
    public partial bool HasOptimizableJobs { get; private set; }

    [ObservableProperty]
    public partial string Summary { get; private set; } = "";

    /// <summary>One-off message such as "2 files were skipped".</summary>
    [ObservableProperty]
    public partial string? Notice { get; set; }

    /// <summary>"Lemon 0.4.0 is available", once the startup check finds a newer version.</summary>
    [ObservableProperty]
    public partial string? UpdateMessage { get; set; }

    /// <summary>Which missing tools would let Lemon handle more of the queued files.</summary>
    [ObservableProperty]
    public partial string? MissingToolsMessage { get; private set; }

    /// <summary>Adds files (and the supported files inside any folders) to the queue.</summary>
    /// <param name="quickTarget">From "Convert with Lemon › To PNG": use a preset for this format and start right away.</param>
    /// <param name="startWith">
    /// From the conversion wheel: use this preset and start right away. Files it doesn't fit are only queued.
    /// </param>
    /// <returns>How many files were started.</returns>
    public int Add(IEnumerable<string> paths, string? quickTarget = null, Preset? startWith = null)
    {
        var skipped = 0;
        var missingTools = new HashSet<ExternalTool>();
        var added = new List<JobViewModel>();
        var editable = new Dictionary<string, JobViewModel>(StringComparer.OrdinalIgnoreCase);
        foreach (var j in Jobs.Where(j => j.IsEditable))
            editable.TryAdd(j.InputPath, j);
        // Presets depend only on the file type, so a folder of 5,000 photos is one lookup, not 5,000.
        var presetsByType = new Dictionary<string, IReadOnlyList<Preset>>();
        _bulk = true;
        try
        {
            foreach (var path in ExpandFolders(paths))
            {
                if (!FormatCatalog.IsSupported(path))
                {
                    skipped++;
                    continue;
                }
                if (editable.TryGetValue(path, out var waiting))
                {
                    // Already waiting in the queue. A wheel format or right-click "To PNG" still means "convert it now
                    // like this", so reuse that row instead of silently doing nothing.
                    if (startWith is not null && waiting.AvailablePresets.FirstOrDefault(p => p.Id == startWith.Id) is { } fits)
                    {
                        waiting.SelectedPreset = fits;
                        added.Add(waiting);
                    }
                    else if (startWith is null && quickTarget is not null)
                    {
                        waiting.SelectedPreset = DefaultPreset(path, waiting.AvailablePresets, quickTarget);
                        added.Add(waiting);
                    }
                    continue;
                }

                var type = FormatCatalog.NormalizeExtension(path);
                if (!presetsByType.TryGetValue(type, out var presets))
                {
                    presetsByType[type] = presets = PresetsFor(path);
                    if (presets.Count == 0)
                        missingTools.UnionWith(_conversion.MissingToolsFor(path));
                }
                if (presets.Count == 0)
                {
                    skipped++;
                    continue;
                }
                var preset = presets.FirstOrDefault(p => p.Id == startWith?.Id) ?? DefaultPreset(path, presets, quickTarget);
                var job = new JobViewModel(path, presets, preset);
                job.PropertyChanged += OnJobChanged;
                Jobs.Add(job);
                editable.TryAdd(path, job);
                if (startWith is null || preset?.Id == startWith.Id)
                    added.Add(job);
            }
        }
        finally
        {
            _bulk = false;
            UpdateSummary();
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

        if (quickTarget is null && startWith is null)
            return 0;
        Enqueue(added);
        return added.Count(j => j.IsActive);
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

    public void ClearFinished() => RemoveAll(j => j.IsDone);

    public void ClearAll() => RemoveAll(j => !j.IsActive);

    private void RemoveAll(Func<JobViewModel, bool> which)
    {
        _bulk = true;
        try
        {
            foreach (var job in Jobs.Where(which).ToList())
                Remove(job);
        }
        finally
        {
            _bulk = false;
            UpdateSummary();
        }
    }

    /// <summary>The kinds of files in the queue that can still be changed, with presets that fit them.</summary>
    public IEnumerable<(MediaKind Kind, IReadOnlyList<Preset> Presets)> PresetChoicesByKind() =>
        Jobs.Where(j => j.IsEditable)
            .GroupBy(j => j.Kind)
            .OrderBy(g => g.Key)
            .Select(g => (g.Key, (IReadOnlyList<Preset>)g.SelectMany(j => j.AvailablePresets).DistinctBy(p => p.Id).ToList()));

    /// <summary>Switches every file that can be optimized to Optimize (Balanced) and starts them.</summary>
    public void OptimizeAll()
    {
        var jobs = new List<JobViewModel>();
        foreach (var job in Jobs.Where(j => j.IsEditable))
        {
            if ((job.AvailablePresets.FirstOrDefault(p => p.Id == BuiltInPresets.OptimizeId)
                 ?? job.AvailablePresets.FirstOrDefault(p => p.IsOptimize)) is not { } optimize)
                continue;
            job.SelectedPreset = optimize;
            jobs.Add(job);
        }
        Enqueue(jobs);
    }

    public void ApplyToAll(MediaKind kind, Preset preset)
    {
        foreach (var job in Jobs.Where(j => j.IsEditable && j.Kind == kind && j.AvailablePresets.Contains(preset)))
            job.SelectedPreset = preset;
    }

    /// <summary>Recomputes which presets fit each file, after presets change or tools are installed.</summary>
    public void RefreshPresets()
    {
        var presetsByType = new Dictionary<string, IReadOnlyList<Preset>>();
        _bulk = true;
        try
        {
            foreach (var job in Jobs.Where(j => j.IsEditable))
            {
                var type = FormatCatalog.NormalizeExtension(job.InputPath);
                if (!presetsByType.TryGetValue(type, out var presets))
                    presetsByType[type] = presets = PresetsFor(job.InputPath);
                job.UpdatePresets(presets, DefaultPreset(job.InputPath, presets, null));
            }
        }
        finally
        {
            _bulk = false;
        }
        UpdateSummary();
    }

    private void Enqueue(IReadOnlyList<JobViewModel> jobs)
    {
        _bulk = true;
        try
        {
            foreach (var job in jobs.Where(j => j.SelectedPreset is not null))
            {
                job.Status = JobStatus.Queued;
                _batch.Add(job);
            }
        }
        finally
        {
            _bulk = false;
        }
        UpdateSummary();
        if (_batch.Count > 0)
            UpdateBatchProgress();
        Pump();
    }

    /// <summary>Starts queued jobs until the parallel limit is reached. Runs on the UI thread only.</summary>
    private void Pump()
    {
        while (_running.Count < Math.Max(1, _settings.MaxParallelJobs)
               && Jobs.FirstOrDefault(j => j.Status == JobStatus.Queued && !_running.ContainsKey(j)) is { } job)
            _running[job] = RunAsync(job);
        IsBusy = _running.Count > 0;

        if (IsBusy || _batch.Count == 0)
            return;
        var finished = _batch.ToList();
        _batch.Clear();
        BatchProgress = null;
        BatchStatus = "";
        BatchFinished?.Invoke(finished);
    }

    private void UpdateBatchProgress()
    {
        var finished = _batch.Count(j => !j.IsActive);
        var progress = _batch.Sum(j => j.IsActive ? (j.Status == JobStatus.Converting ? j.Progress : 0) : 1) / _batch.Count;
        BatchProgress = progress;
        BatchStatus = $"{BatchVerb(_batch)} · {finished} of {_batch.Count} done · {progress:0%}";
    }

    /// <summary>"Removing background", "Upscaling", "Optimizing", "Resizing" or "Converting", by what every job in the batch does.</summary>
    public static string BatchVerb(IEnumerable<JobViewModel> jobs) =>
        jobs.All(j => j.SelectedPreset?.RemoveBackground is not null) ? "Removing background"
        : jobs.All(j => j.SelectedPreset?.Upscale is not null) ? "Upscaling"
        : jobs.All(j => j.SelectedPreset?.IsOptimize == true) ? "Optimizing"
        : jobs.All(j => j.SelectedPreset?.IsResize == true) ? "Resizing"
        : "Converting";

    private async Task RunAsync(JobViewModel job)
    {
        // Let Pump record this task before it can finish and remove itself.
        await Task.Yield();
        await job.RunAsync(_conversion, _settings.OutputFolder);
        _running.Remove(job);
        Pump();
    }

    /// <summary>The presets that can convert this file: Optimize, then the same kind of output, then the rest.</summary>
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
            .Where(p => p.IsOptimize || kind != MediaKind.Video || p.TargetKind != MediaKind.Image || p.Fps is not null)
            // ...and the other way round: animation presets mean nothing for a still image.
            .Where(p => kind != MediaKind.Image || p.Fps is null || extension is "gif" or "webp")
            .OrderBy(p => !p.IsOptimize)
            .ThenBy(p => p.TargetKind != kind)
            .ToList();
    }

    private Preset? DefaultPreset(string path, IReadOnlyList<Preset> presets, string? quickTarget)
    {
        var kind = FormatCatalog.KindOf(path) ?? MediaKind.Document;
        var last = _settings.LastPresetByKind.TryGetValue(kind, out var id) ? presets.FirstOrDefault(p => p.Id == id) : null;

        if (quickTarget is not null)
        {
            var matching = presets.Where(p => FormatCatalog.NormalizeExtension(p.TargetExtension) == quickTarget).ToList();
            return matching.FirstOrDefault(p => p.Id == last?.Id)
                // "--optimize" means Balanced; the other strengths are picked on the wheel or in the list.
                ?? matching.FirstOrDefault(p => p.Id == BuiltInPresets.OptimizeId)
                // "To PNG" means a plain PNG, not the background cutout that also writes PNG.
                ?? matching.FirstOrDefault(p => p.TargetKind == kind && p.RemoveBackground is null)
                ?? matching.FirstOrDefault(p => p.RemoveBackground is null)
                ?? matching.FirstOrDefault();
        }

        var extension = FormatCatalog.NormalizeExtension(path);
        // Optimize, Resize and Remove background are listed first but only become the default once the user picks them
        // (or they're all there is).
        return last
            ?? presets.FirstOrDefault(p => !p.IsOptimize && !p.IsResize && p.RemoveBackground is null && FormatCatalog.NormalizeExtension(p.TargetExtension) != extension)
            ?? presets.FirstOrDefault();
    }

    private static bool HasOptions(Preset p) =>
        p.Quality is not null || p.MaxWidth is not null || p.MaxHeight is not null || p.AudioBitrateKbps is not null
        || p.VideoCrf is not null || p.Fps is not null || p.StripMetadata || p.RemoveBackground is not null;

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
        if (!_bulk)
            UpdateSummary();
    }

    private void OnJobChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Bulk changes (a wheel drop or right-click reusing a waiting row, a preset refresh's fallback) aren't picks.
        if (sender is not JobViewModel job || _bulk)
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
            _summaryDue = true;
        else if (!(e.PropertyName == nameof(JobViewModel.Progress) && _batch.Contains(job)))
            return;

        // Both walk the whole queue, and a batch of thousands of pictures changes several times per file. After every
        // change (or even once per pass of the UI thread, since jobs finish spread out) a big batch was quadratic; at
        // most every 100 ms it's not. Each row's own bar and status still follow its job straight away.
        if (!_refresh.IsRunning)
            _refresh.Start();
    }

    private void UpdateSummary()
    {
        IsEmpty = Jobs.Count == 0;
        HasStartableJobs = Jobs.Any(j => j.IsStartable);
        HasFinishedJobs = Jobs.Any(j => j.IsDone);
        HasOptimizableJobs = Jobs.Any(j => j.IsEditable && j.AvailablePresets.Any(p => p.IsOptimize));

        var done = Jobs.Count(j => j.IsDone);
        var failed = Jobs.Count(j => j.IsFailed);
        var parts = new List<string> { Jobs.Count == 1 ? "1 file" : $"{Jobs.Count} files" };
        if (done > 0)
            parts.Add($"{done} converted");
        if (failed > 0)
            parts.Add($"{failed} failed");
        Summary = Jobs.Count == 0 ? "" : string.Join(" · ", parts);

        // Once per file type, not per file: this runs after every status change.
        var missing = Jobs.Select(j => FormatCatalog.NormalizeExtension(j.InputPath)).Distinct()
            .SelectMany(_conversion.MissingToolsFor).Distinct().ToList();
        MissingToolsMessage = missing.Count == 0
            ? null
            : "Install " + string.Join(" and ", missing.Select(ToolLocator.DisplayName)) + " to unlock more formats for these files: "
              + string.Join("  ·  ", missing.Select(ToolLocator.InstallHint));
    }
}
