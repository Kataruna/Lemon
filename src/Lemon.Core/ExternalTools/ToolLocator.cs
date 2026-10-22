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
    public static string InstallHint(ExternalTool tool) => tool switch
    {
        ExternalTool.Ffmpeg or ExternalTool.Ffprobe => "winget install Gyan.FFmpeg",
        ExternalTool.Pandoc => "winget install JohnMacFarlane.Pandoc",
        ExternalTool.LibreOffice => "winget install TheDocumentFoundation.LibreOffice",
        // Not on winget; Artifex only publishes installers on its own site.
        ExternalTool.Ghostscript => "https://ghostscript.com/releases/gsdnld.html",
        _ => "",
    };

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
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
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
