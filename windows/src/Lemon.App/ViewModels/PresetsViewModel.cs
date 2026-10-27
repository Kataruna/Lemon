using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Lemon.App.ViewModels;

public sealed record FormatOption(string Extension, string Label);

public sealed record BitrateOption(int? Kbps, string Label);

/// <summary>Lists presets and edits the selected one. Built-in presets are read-only; duplicate them to customize.</summary>
public sealed partial class PresetsViewModel : ObservableObject
{
    private static readonly string[] LossyAudio = ["mp3", "m4a", "ogg", "opus"];
    private static readonly string[] QualityImages = ["jpg", "webp", "avif"];

    private readonly PresetStore _store;

    public PresetsViewModel(PresetStore store)
    {
        _store = store;
        Reload(selectId: null);
    }

    public ObservableCollection<Preset> Items { get; } = [];

    /// <summary>Resize: pictures keep their own format and are only shrunk to the max size.</summary>
    private static readonly FormatOption KeepFormat = new(Preset.SameFormat, "Keep the original format (resize pictures)");

    public static IReadOnlyList<FormatOption> TargetFormats { get; } =
        [KeepFormat, .. new[] { "png", "jpg", "webp", "avif", "gif", "bmp", "tiff", "ico",
                "mp3", "m4a", "opus", "ogg", "flac", "wav", "aiff",
                "mp4", "mkv", "webm", "mov",
                "pdf", "docx", "odt", "rtf", "html", "md", "txt", "epub" }
            .Select(ext => FormatCatalog.Find(ext)!)
            .Select(f => new FormatOption(f.Extension, $"{f.DisplayName} (.{f.Extension}) · {f.Kind}"))];

    public static IReadOnlyList<BitrateOption> Bitrates { get; } =
        new int?[] { null, 64, 96, 128, 160, 192, 256, 320 }
            .Select(b => new BitrateOption(b, b is null ? "Default" : $"{b} kbps"))
            .ToList();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(IsEditable), nameof(IsBuiltIn))]
    public partial Preset? Selected { get; set; }

    [ObservableProperty]
    public partial string Name { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowQuality), nameof(ShowSize), nameof(ShowAudioBitrate), nameof(ShowVideoQuality),
        nameof(ShowFps), nameof(ShowMetadata), nameof(HasNoOptions), nameof(NeedsSize))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial FormatOption? Target { get; set; }

    [ObservableProperty]
    public partial double Quality { get; set; } = 85;

    [ObservableProperty]
    public partial bool UseQuality { get; set; }

    /// <summary>NaN means "no limit" (an empty NumberBox).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsSize))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial double MaxWidth { get; set; } = double.NaN;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsSize))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial double MaxHeight { get; set; } = double.NaN;

    [ObservableProperty]
    public partial BitrateOption? Bitrate { get; set; }

    [ObservableProperty]
    public partial double VideoCrf { get; set; } = 23;

    [ObservableProperty]
    public partial double Fps { get; set; } = double.NaN;

    [ObservableProperty]
    public partial bool StripMetadata { get; set; }

    public bool HasSelection => Selected is not null;
    public bool IsBuiltIn => Selected?.IsBuiltIn == true;
    public bool IsEditable => Selected is { IsBuiltIn: false };

    private string TargetExt => Target?.Extension ?? "";
    private bool KeepsFormat => TargetExt == Preset.SameFormat;
    private MediaKind TargetKind => KeepsFormat ? MediaKind.Image : FormatCatalog.KindOf(TargetExt) ?? MediaKind.Document;

    /// <summary>
    /// Keeping the format, the size is the whole point: without one the preset would be an Optimize preset instead.
    /// </summary>
    public bool NeedsSize => KeepsFormat && ToInt(MaxWidth) is null && ToInt(MaxHeight) is null;

    // Keeping the format, the quality is used for pictures that have one (JPEG, WebP, AVIF).
    public bool ShowQuality => QualityImages.Contains(TargetExt) || KeepsFormat;
    public bool ShowSize => TargetKind is MediaKind.Image or MediaKind.Video;
    public bool ShowAudioBitrate => LossyAudio.Contains(TargetExt) || (TargetKind == MediaKind.Video && TargetExt != "gif");
    public bool ShowVideoQuality => TargetKind == MediaKind.Video && TargetExt != "gif";
    public bool ShowFps => TargetKind == MediaKind.Video || TargetExt == "gif";
    public bool ShowMetadata => TargetKind != MediaKind.Document;
    public bool HasNoOptions => TargetKind == MediaKind.Document;

    partial void OnSelectedChanged(Preset? value)
    {
        if (value is null)
            return;
        Name = value.Name;
        Target = TargetFormats.FirstOrDefault(f => f.Extension == FormatCatalog.NormalizeExtension(value.TargetExtension));
        UseQuality = value.Quality is not null;
        Quality = value.Quality ?? 85;
        MaxWidth = value.MaxWidth ?? double.NaN;
        MaxHeight = value.MaxHeight ?? double.NaN;
        Bitrate = Bitrates.FirstOrDefault(b => b.Kbps == value.AudioBitrateKbps) ?? Bitrates[0];
        VideoCrf = value.VideoCrf ?? 23;
        Fps = value.Fps ?? double.NaN;
        StripMetadata = value.StripMetadata;
    }

    [RelayCommand]
    private void New()
    {
        var preset = new Preset { Name = "New preset", TargetExtension = "jpg", Quality = 85 };
        _store.Upsert(preset);
        Reload(preset.Id);
    }

    [RelayCommand]
    private void Duplicate()
    {
        if (Selected is null)
            return;
        var copy = _store.Duplicate(Selected);
        Reload(copy.Id);
    }

    [RelayCommand]
    private void Delete()
    {
        if (Selected is not { IsBuiltIn: false } preset)
            return;
        var index = Items.IndexOf(preset);
        _store.Delete(preset.Id);
        // Also take it off the wheel, so it doesn't linger there as a dead entry.
        App.Settings.Wheel.Forget(preset.Id);
        App.Settings.Save();
        Reload(Items.ElementAtOrDefault(Math.Max(0, index - 1))?.Id);
    }

    private bool CanSave() => !NeedsSize;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        if (Selected is not { IsBuiltIn: false } current || Target is null || NeedsSize)
            return;

        var updated = current with
        {
            Name = string.IsNullOrWhiteSpace(Name) ? current.Name : Name.Trim(),
            TargetExtension = Target.Extension,
            Quality = ShowQuality && UseQuality ? (int)Math.Round(Quality) : null,
            MaxWidth = ShowSize ? ToInt(MaxWidth) : null,
            MaxHeight = ShowSize ? ToInt(MaxHeight) : null,
            AudioBitrateKbps = ShowAudioBitrate ? Bitrate?.Kbps : null,
            VideoCrf = ShowVideoQuality ? (int)Math.Round(VideoCrf) : null,
            Fps = ShowFps ? ToInt(Fps) : null,
            StripMetadata = ShowMetadata && StripMetadata,
        };
        _store.Upsert(updated);
        Reload(updated.Id);
    }

    private void Reload(string? selectId)
    {
        Items.Clear();
        // Optimize, Remove background and Upscale have nothing to tune here (the cutout ignores size and quality, a copy
        // retargeted to another format would match no converter, and the AI model isn't something this editor picks),
        // so they aren't listed for editing or duplicating. Resize is: a size, kept in the picture's own format.
        foreach (var preset in _store.All.Where(p => !p.IsOptimize && p.RemoveBackground is null && p.Upscale is null)
                     .OrderBy(p => p.IsResize ? MediaKind.Image : p.TargetKind).ThenBy(p => p.IsBuiltIn))
            Items.Add(preset);
        Selected = Items.FirstOrDefault(p => p.Id == selectId) ?? Items.FirstOrDefault();
    }

    private static int? ToInt(double value) => double.IsNaN(value) || value <= 0 ? null : (int)Math.Round(value);
}
