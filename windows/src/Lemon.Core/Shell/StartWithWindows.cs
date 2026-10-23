using Microsoft.Win32;

namespace Lemon.Core.Shell;

/// <summary>Starts Lemon quietly in the notification area when the user signs in (per user, no admin).</summary>
public static class StartWithWindows
{
    public const string BackgroundArgument = "--background";

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Lemon";

    public static bool IsEnabled(RegistryKey? currentUser = null)
    {
        using var run = (currentUser ?? Registry.CurrentUser).OpenSubKey(RunKey);
        return run?.GetValue(ValueName) is string;
    }

    public static void Enable(string exePath, RegistryKey? currentUser = null)
    {
        using var run = (currentUser ?? Registry.CurrentUser).CreateSubKey(RunKey);
        run.SetValue(ValueName, $"\"{exePath}\" {BackgroundArgument}");
    }

    public static void Disable(RegistryKey? currentUser = null)
    {
        using var run = (currentUser ?? Registry.CurrentUser).OpenSubKey(RunKey, writable: true);
        run?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
