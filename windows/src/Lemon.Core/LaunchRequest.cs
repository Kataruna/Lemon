namespace Lemon.Core;

/// <summary>
/// What Lemon was asked to do on launch: <c>Lemon.exe [--background] [--to &lt;ext&gt; | --optimize | --preset &lt;id&gt;] file...</c>.
/// With <c>--to</c> the files are queued with a matching preset and converted straight away, without
/// showing the window; <c>--optimize</c> does the same with the Optimize preset (same format, smaller file)
/// and <c>--preset</c> with that preset (Remove background, Upscale).
/// <c>--background</c> (used when starting with Windows) starts in the tray.
/// </summary>
public sealed record LaunchRequest(IReadOnlyList<string> Files, string? QuickTarget, bool Background = false, string? PresetId = null)
{
    public const string OptimizeArgument = "--optimize";
    public const string PresetArgument = "--preset";

    public static LaunchRequest Empty { get; } = new([], null);

    public bool IsEmpty => Files.Count == 0;

    /// <summary>Whether this launch should leave the window closed (the work happens in the tray).</summary>
    public bool StaysInBackground => Background || QuickTarget is not null || PresetId is not null;

    public static LaunchRequest Parse(IEnumerable<string> args)
    {
        var files = new List<string>();
        string? target = null;
        string? preset = null;
        var background = false;
        using var e = args.GetEnumerator();
        while (e.MoveNext())
        {
            if (e.Current.Equals("--to", StringComparison.OrdinalIgnoreCase) && e.MoveNext())
                target = FormatCatalog.NormalizeExtension(e.Current);
            else if (e.Current.Equals(OptimizeArgument, StringComparison.OrdinalIgnoreCase))
                target = Preset.SameFormat;
            else if (e.Current.Equals(PresetArgument, StringComparison.OrdinalIgnoreCase) && e.MoveNext())
                preset = e.Current;
            else if (e.Current.Equals(StartWithWindows.BackgroundArgument, StringComparison.OrdinalIgnoreCase))
                background = true;
            else if (!e.Current.StartsWith("--", StringComparison.Ordinal))
                files.Add(e.Current);
        }
        return new LaunchRequest(files, target, background, preset);
    }
}
