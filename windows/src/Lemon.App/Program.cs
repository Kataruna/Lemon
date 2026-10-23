using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace Lemon.App;

public static class Program
{
    /// <summary>
    /// Explorer starts one Lemon process per selected file, so every launch after the first hands its
    /// arguments to the running instance and exits. That way a multi-file right-click fills one queue.
    /// </summary>
    [STAThread]
    private static int Main(string[] args)
    {
        // Setup/uninstall hooks: do the job and exit without starting the app.
        switch (args)
        {
            case ["--register-context-menu"]:
                var conversion = new ConversionService(new ToolLocator(tool => AppSettings.Load().ToolPaths.GetValueOrDefault(tool)));
                ContextMenu.Register(Environment.ProcessPath!, (from, to) => conversion.FindConverter(from, to) is not null);
                return 0;
            case ["--unregister"]:
                ContextMenu.Unregister();
                StartWithWindows.Disable();
                return 0;
        }

        WinRT.ComWrappersSupport.InitializeComWrappers();

        var mainInstance = AppInstance.FindOrRegisterForKey("Lemon.Main");
        if (!mainInstance.IsCurrent)
        {
            // Lets the running instance pull its window to the front once it receives the files.
            Native.AllowSetForegroundWindow(mainInstance.ProcessId);
            var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
            Task.Run(() => mainInstance.RedirectActivationToAsync(activation).AsTask()).Wait();
            return 0;
        }

        // Setup and the uninstaller look for this mutex and ask the user to quit Lemon first.
        using var runningMutex = new Mutex(initiallyOwned: false, "Lemon.App");
        Task.Run(RemoveOldUnpackedCopies);

        // Subscribe before starting XAML so launches that race our startup aren't lost.
        mainInstance.Activated += (_, e) => App.HandleActivation(e);
        App.InitialLaunch = LaunchRequest.Parse(args);

        Application.Start(callbackParams =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            _ = new App();
        });
        return 0;
    }

    /// <summary>
    /// The single-file portable build unpacks itself to %TEMP%\.net\Lemon\&lt;hash&gt;, a new folder (~230 MB)
    /// for every version. Delete the ones older versions left behind; folders still in use are skipped.
    /// </summary>
    private static void RemoveOldUnpackedCopies()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
        if (current.Parent is not { Name: "Lemon", Parent.Name: ".net" } unpackRoot)
            return; // Installed or Debug build: nothing was unpacked.

        foreach (var old in unpackRoot.EnumerateDirectories().Where(d => !string.Equals(d.FullName, current.FullName, StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                old.Delete(recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Another Lemon version is running from it; try again next time.
            }
        }
    }
}
