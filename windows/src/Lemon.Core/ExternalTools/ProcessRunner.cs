using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Lemon.Core.ExternalTools;

public sealed record ProcessResult(int ExitCode, string StandardError);

public static class ProcessRunner
{
    /// <summary>
    /// Runs a console program without a window. Each stdout line goes to <paramref name="onOutputLine"/>.
    /// Cancelling kills the whole process tree and throws <see cref="OperationCanceledException"/>.
    /// </summary>
    public static async Task<ProcessResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        Action<string>? onOutputLine = null,
        CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                onOutputLine?.Invoke(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null)
                return;
            lock (stderr)
            {
                stderr.AppendLine(e.Data);
                // Keep only the tail; ffmpeg is chatty and the end has the useful error.
                if (stderr.Length > 16_000)
                    stderr.Remove(0, stderr.Length - 8_000);
            }
        };

        process.Start();
        ChildProcessJob.Add(process);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
                // Killing only asks: until it's gone the tool still holds its output, which the caller is about to delete.
                process.WaitForExit(3000);
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or AggregateException)
            {
                // Exited meanwhile, or one of its children did. Canceled either way, not a failed job.
            }
            throw;
        }

        // Make sure the async readers have flushed before reading stderr.
        process.WaitForExit();
        lock (stderr)
            return new ProcessResult(process.ExitCode, stderr.ToString());
    }

    /// <summary>Shortens tool output to its last few lines for display in the UI.</summary>
    public static string Summarize(string stderr, int lines = 3)
    {
        var tail = stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).TakeLast(lines);
        return string.Join(" ", tail);
    }
}
