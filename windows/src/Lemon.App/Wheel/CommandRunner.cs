using System.Diagnostics;
using System.Text;

namespace Lemon.App.Wheel;

/// <summary>Runs a command from the Commands ring, once per dragged item, in PowerShell, without waiting for it.</summary>
internal static class CommandRunner
{
    /// <param name="items">Dragged file or folder paths, or links.</param>
    public static void Run(WheelCommand command, IReadOnlyList<string> items)
    {
        foreach (var item in items)
        {
            var files = command.RunsOn == RunsOn.Folders && command.Line.Contains(CommandToken.Files.Text()) ? FilesIn(item) : [];
            var line = CommandLine.Build(command.Line, item, files);
            // A program's exit code becomes PowerShell's; with no program, a failed cmdlet is exit code 1. Not
            // $ErrorActionPreference = 'Stop': PowerShell 5.1 would stop on a program that only writes progress to stderr.
            var script = $"{line}\n$ok = $?; if ($LASTEXITCODE) {{ exit $LASTEXITCODE }}; if ($null -eq $LASTEXITCODE -and -not $ok) {{ exit 1 }}";
            // A script file, so no quote in the line has to survive Windows' command-line parsing, and errors come back as
            // text (with -EncodedCommand they come back as XML).
            var file = Path.Combine(Path.GetTempPath(), "Lemon", "Commands", Guid.NewGuid().ToString("n") + ".ps1");
            var start = new ProcessStartInfo("powershell.exe")
            {
                ArgumentList = { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", file },
                // In the folder; a file's own folder; a link's in the user's folder.
                WorkingDirectory = command.RunsOn switch
                {
                    RunsOn.Folders => item,
                    RunsOn.Files => Path.GetDirectoryName(item)!,
                    _ => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                },
                UseShellExecute = false,
                CreateNoWindow = true,
                // All three: Lemon has no console, so a stream left alone would be an invalid handle some programs fail on.
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            Process process;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                // With a BOM, or PowerShell 5.1 reads ’ and é wrong.
                File.WriteAllText(file, script, Encoding.UTF8);
                process = Process.Start(start)!;
            }
            catch (Exception ex)
            {
                Delete(file);
                Failed(command, ex.Message);
                continue;
            }
            process.StandardInput.Close();
            _ = process.StandardOutput.BaseStream.CopyToAsync(Stream.Null);
            _ = WatchAsync(command, process, file);
        }
    }

    private static async Task WatchAsync(WheelCommand command, Process process, string file)
    {
        using (process)
        {
            // Read while it runs (a full stderr pipe would stall it), but go by its exit: a program it started, like VS
            // Code for "code .", keeps the pipe open until that program quits.
            var reading = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            Delete(file);
            if (process.ExitCode == 0)
                return;
            await Task.WhenAny(reading, Task.Delay(500));
            var errors = reading.IsCompletedSuccessfully ? reading.Result : "";
            // Without the "At …ps1:1 char:1" and "+ ~~~" lines under a PowerShell error, which point into our script.
            var tail = string.Join('\n', errors.Split('\n').Select(l => l.TrimEnd()).Where(l =>
                l.Length > 0 && !l.TrimStart().StartsWith('+') && !l.StartsWith("At ") && !l.StartsWith("char:") && !l.Contains(file))).Trim();
            // Notifications show about 250 characters.
            Failed(command, tail.Length == 0 ? $"It stopped with exit code {process.ExitCode}." : tail.Length > 200 ? "…" + tail[^200..] : tail);
        }
    }

    /// <summary>The files directly inside a folder, by name, without hidden ones or subfolders.</summary>
    private static List<string> FilesIn(string folder)
    {
        // ponytail: top level only; recurse if someone wants a whole tree in one command.
        try
        {
            return [.. Directory.EnumerateFiles(folder, "*", new EnumerationOptions()).Order(StringComparer.OrdinalIgnoreCase)];
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static void Delete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception)
        {
            // Left in the temp folder; nothing depends on it being gone.
        }
    }

    private static void Failed(WheelCommand command, string detail) =>
        App.Window?.DispatcherQueue.TryEnqueue(() => App.Notify($"“{command.Name}” failed", detail));
}
