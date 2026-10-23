namespace Lemon.Core.Configuration;

public sealed class AppSettings
{
    /// <summary>Where converted files go. Null means next to the source file.</summary>
    public string? OutputFolder { get; set; }

    public int MaxParallelJobs { get; set; } = Math.Clamp(Environment.ProcessorCount / 4, 1, 4);

    /// <summary>Paths the user picked by hand, keyed by tool. They win over PATH lookups.</summary>
    public Dictionary<ExternalTool, string> ToolPaths { get; set; } = [];

    /// <summary>The preset most recently chosen for each kind of input, used for newly added files.</summary>
    public Dictionary<MediaKind, string> LastPresetByKind { get; set; } = [];

    /// <summary>Open the conversion wheel by shaking the mouse while dragging files.</summary>
    public bool WheelOnShake { get; set; } = true;

    /// <summary>Key that opens the conversion wheel while dragging files (e.g. "Space"), or null for none.</summary>
    public string? WheelKey { get; set; } = "Space";

    /// <summary>Closing the window hides Lemon to the notification area instead of quitting.</summary>
    public bool KeepRunningInTray { get; set; } = true;

    /// <summary>Whether the "Lemon is still running" notice has been shown after the first close.</summary>
    public bool TrayNoticeShown { get; set; }

    /// <summary>The conversion wheel's look and rings.</summary>
    public WheelSettings Wheel { get; set; } = new();

    public static string DefaultPath { get; } = Path.Combine(JsonFileStore.DefaultDirectory, "settings.json");

    public static AppSettings Load(string? path = null) => JsonFileStore.Load<AppSettings>(path ?? DefaultPath) ?? new();

    public void Save(string? path = null) => JsonFileStore.Save(path ?? DefaultPath, this);
}
