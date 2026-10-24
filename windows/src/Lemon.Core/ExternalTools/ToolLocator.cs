namespace Lemon.Core.ExternalTools;

public enum ExternalTool
{
    Ffmpeg,
    Ffprobe,
    Pandoc,
    LibreOffice,
    Ghostscript,
}

/// <summary>Finds the external programs Lemon drives: user override first, then PATH, then usual install folders.</summary>
/// <param name="searchSystem">False limits lookups to <paramref name="overrides"/>; used by tests.</param>
public sealed class ToolLocator(Func<ExternalTool, string?>? overrides = null, bool searchSystem = true)
{
    /// <summary>The tool's winget package, or null when it isn't on winget (see <see cref="DownloadPage"/>).</summary>
    public static string? WingetId(ExternalTool tool) => tool switch
    {
        ExternalTool.Ffmpeg or ExternalTool.Ffprobe => "Gyan.FFmpeg",
        ExternalTool.Pandoc => "JohnMacFarlane.Pandoc",
        ExternalTool.LibreOffice => "TheDocumentFoundation.LibreOffice",
        _ => null,
    };

    /// <summary>Where to download a tool that isn't on winget.</summary>
    public static string? DownloadPage(ExternalTool tool) => tool switch
    {
        // Artifex only publishes installers on its own site.
        ExternalTool.Ghostscript => "https://ghostscript.com/releases/gsdnld.html",
        _ => null,
    };

    public static string InstallHint(ExternalTool tool) =>
        WingetId(tool) is { } id ? "winget install " + id : DownloadPage(tool) ?? "";

    public static string DisplayName(ExternalTool tool) => tool switch
    {
        ExternalTool.Ffmpeg => "FFmpeg",
        ExternalTool.Ffprobe => "FFprobe",
        ExternalTool.Pandoc => "Pandoc",
        ExternalTool.LibreOffice => "LibreOffice",
        ExternalTool.Ghostscript => "Ghostscript",
        _ => tool.ToString(),
    };

    public string? Find(ExternalTool tool)
    {
        if (overrides?.Invoke(tool) is { } custom && File.Exists(custom))
            return custom;
        if (!searchSystem)
            return null;

        // ffprobe ships alongside ffmpeg, so prefer the copy next to whichever ffmpeg we're using.
        if (tool == ExternalTool.Ffprobe && Find(ExternalTool.Ffmpeg) is { } ffmpeg)
        {
            var sibling = Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe");
            if (File.Exists(sibling))
                return sibling;
        }

        var exe = tool switch
        {
            ExternalTool.Ffmpeg => "ffmpeg.exe",
            ExternalTool.Ffprobe => "ffprobe.exe",
            ExternalTool.Pandoc => "pandoc.exe",
            ExternalTool.LibreOffice => "soffice.exe",
            ExternalTool.Ghostscript => "gswin64c.exe",
            _ => throw new ArgumentOutOfRangeException(nameof(tool)),
        };

        return SearchPath(exe) ?? KnownLocations(tool).Select(dir => Path.Combine(dir, exe)).FirstOrDefault(File.Exists);
    }

    private static string? SearchPath(string exe)
    {
        // Lemon often runs for days in the tray, and its own PATH is a snapshot from when it started. Also read the
        // saved user and machine PATH, so a tool installed since then (e.g. with winget) is found without a restart.
        var path = string.Join(Path.PathSeparator,
            Environment.GetEnvironmentVariable("PATH"),
            Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User),
            Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine));
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var candidate = Path.Combine(Environment.ExpandEnvironmentVariables(dir.Trim('"')), exe);
                if (File.Exists(candidate))
                    return candidate;
            }
            catch (ArgumentException)
            {
                // Malformed PATH entries are common on Windows; skip them.
            }
        }
        return null;
    }

    private static IEnumerable<string> KnownLocations(ExternalTool tool)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        switch (tool)
        {
            case ExternalTool.Ffmpeg or ExternalTool.Ffprobe:
                yield return Path.Combine(local, "Microsoft", "WinGet", "Links");
                yield return Path.Combine(programFiles, "ffmpeg", "bin");
                break;
            case ExternalTool.Pandoc:
                yield return Path.Combine(local, "Pandoc");
                yield return Path.Combine(programFiles, "Pandoc");
                break;
            case ExternalTool.LibreOffice:
                yield return Path.Combine(programFiles, "LibreOffice", "program");
                break;
            case ExternalTool.Ghostscript:
                // Each version installs into its own folder, e.g. C:\Program Files\gs\gs10.05.1\bin; prefer the newest.
                var gs = Path.Combine(programFiles, "gs");
                if (!Directory.Exists(gs))
                    break;
                foreach (var version in Directory.EnumerateDirectories(gs, "gs*")
                             .OrderByDescending(d => Version.TryParse(Path.GetFileName(d)[2..], out var v) ? v : new Version()))
                    yield return Path.Combine(version, "bin");
                break;
        }
    }
}
