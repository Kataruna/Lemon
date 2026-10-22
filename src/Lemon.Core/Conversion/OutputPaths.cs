namespace Lemon.Core.Conversion;

public static class OutputPaths
{
    private static readonly Lock Gate = new();

    /// <summary>
    /// Picks a file name for the converted output that never overwrites anything ("photo.png", then
    /// "photo (1).png", ...) and claims it by creating an empty placeholder, so parallel jobs can't collide.
    /// </summary>
    /// <param name="suffix">Added to the name before the extension, e.g. " (optimized)".</param>
    public static string Reserve(string inputPath, string targetExtension, string? outputFolder = null, string? suffix = null)
    {
        var folder = string.IsNullOrWhiteSpace(outputFolder) ? Path.GetDirectoryName(Path.GetFullPath(inputPath))! : outputFolder;
        Directory.CreateDirectory(folder);
        var baseName = Path.GetFileNameWithoutExtension(inputPath) + suffix;
        var extension = "." + FormatCatalog.NormalizeExtension(targetExtension);

        lock (Gate)
        {
            for (var n = 0; ; n++)
            {
                var candidate = Path.Combine(folder, n == 0 ? baseName + extension : $"{baseName} ({n}){extension}");
                if (string.Equals(Path.GetFullPath(candidate), Path.GetFullPath(inputPath), StringComparison.OrdinalIgnoreCase))
                    continue;
                try
                {
                    using (new FileStream(candidate, FileMode.CreateNew, FileAccess.Write)) { }
                    return candidate;
                }
                catch (IOException) when (File.Exists(candidate) || Directory.Exists(candidate))
                {
                    // Taken; try the next number.
                }
            }
        }
    }
}
