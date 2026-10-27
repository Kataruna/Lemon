using System.Runtime.InteropServices;

namespace Lemon.App.Wheel;

/// <summary>The files and folders selected in a File Explorer window (its active tab) or on the desktop.</summary>
internal static class ExplorerSelection
{
    /// <summary>
    /// The selection in <paramref name="window"/> when it's Explorer or the desktop, else nothing. Explorer is asked
    /// over COM on a thread of its own, so a hung Explorer can't freeze Lemon: after a second it counts as nothing.
    /// </summary>
    public static async Task<IReadOnlyList<string>> ReadAsync(nint window)
    {
        if (Native.ClassName(window) is not ("CabinetWClass" or "Progman" or "WorkerW"))
            return [];
        // Holding the key while renaming, or in the address or search box, is about the text.
        if (Native.FocusedClass(window) == "Edit")
            return [];

        var result = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                result.TrySetResult(Read(window));
            }
            catch (Exception ex)
            {
                ErrorLog.Write("Reading the Explorer selection", ex);
                result.TrySetResult([]);
            }
        })
        {
            IsBackground = true,
            Name = "Lemon Explorer selection",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        try
        {
            return await result.Task.WaitAsync(TimeSpan.FromSeconds(1));
        }
        catch (TimeoutException)
        {
            return [];
        }
    }

    private static List<string> Read(nint window)
    {
        // Every COM object is let go before this thread ends. Idle in the tray Lemon hardly ever collects garbage, so
        // left to the finalizer they'd keep Explorer's objects alive for days, then be released from a thread that's gone.
        var com = new List<object>();
        dynamic Keep(object value)
        {
            com.Add(value);
            return value;
        }
        try
        {
            dynamic shell = Keep(Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!)!);
            dynamic windows = Keep(shell.Windows());
            dynamic? view = null;
            if (Native.ClassName(window) == "CabinetWClass")
            {
                // Every tab of a window reports the window's handle, so match the tab that's showing.
                var tab = FindWindowEx(window, 0, "ShellTabWindowClass", null);
                foreach (dynamic candidate in windows)
                {
                    Keep(candidate);
                    if ((nint)(long)candidate.HWND == window && (tab == 0 || TabOf(candidate) == tab))
                    {
                        view = Keep(candidate.Document);
                        break;
                    }
                }
            }
            else
            {
                object desktop = 0 /* CSIDL_DESKTOP */, root = Type.Missing;
                int hwnd;
                view = Keep(Keep(windows.FindWindowSW(ref desktop, ref root, 8 /* SWC_DESKTOP */, out hwnd, 1 /* SWFO_NEEDDISPATCH */)).Document);
            }

            var paths = new List<string>();
            if (view is null)
                return paths;
            foreach (dynamic item in Keep(view.SelectedItems()))
            {
                Keep(item);
                // Things with no place on disk (inside a zip, This PC, Control Panel) can't be converted.
                if (item.Path is string path && (File.Exists(path) || Directory.Exists(path)))
                    paths.Add(path);
            }
            return paths;
        }
        finally
        {
            // Cleanup must never cost the selection that was read.
            foreach (var value in com.Distinct(ReferenceEqualityComparer.Instance))
            {
                try
                {
                    if (value is not null && Marshal.IsComObject(value))
                        Marshal.FinalReleaseComObject(value);
                }
                catch (Exception)
                {
                }
            }
        }
    }

    /// <summary>The tab window behind one of ShellWindows' entries.</summary>
    private static unsafe nint TabOf(object window)
    {
        var services = (IServiceProvider)window;
        var service = TopLevelBrowser;
        var shellBrowser = ShellBrowser;
        if (services.QueryService(ref service, ref shellBrowser, out var browser) != 0)
            return 0;
        try
        {
            // IShellBrowser derives from IOleWindow, whose GetWindow comes right after IUnknown's three methods.
            var getWindow = (delegate* unmanaged[Stdcall]<nint, nint*, int>)(*(nint**)browser)[3];
            nint hwnd;
            return getWindow(browser, &hwnd) == 0 ? hwnd : 0;
        }
        finally
        {
            Marshal.Release(browser);
        }
    }

    private static readonly Guid TopLevelBrowser = new("4C96BE40-915C-11CF-99D3-00AA004AE837");
    private static readonly Guid ShellBrowser = new("000214E2-0000-0000-C000-000000000046");

    [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IServiceProvider
    {
        [PreserveSig]
        int QueryService(ref Guid service, ref Guid riid, out nint result);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint FindWindowEx(nint parent, nint after, string className, string? title);
}
