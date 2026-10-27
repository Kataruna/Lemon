namespace Lemon.Core.Configuration;

/// <summary>Appends unexpected errors to %LOCALAPPDATA%\Lemon\errors.log, so crashes can be diagnosed afterwards.</summary>
public static class ErrorLog
{
    private const long MaxSize = 512 * 1024;
    private static readonly Lock Gate = new();

    /// <summary>Tests point it elsewhere, so the errors they cause on purpose stay out of the user's log.</summary>
    public static string FilePath { get; internal set; } = Path.Combine(JsonFileStore.DefaultDirectory, "errors.log");

    public static void Write(string context, Exception exception)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                // Keep the log small: start over once it grows past the limit.
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > MaxSize)
                    File.Delete(FilePath);
                File.AppendAllText(FilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {context}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
            }
        }
        catch (IOException)
        {
            // Logging must never be the thing that crashes the app.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
