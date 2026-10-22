using Microsoft.Win32;

namespace Lemon.Core.Tests;

public class StartWithWindowsTests
{
    [Fact]
    public void WritesAndRemovesRunEntry()
    {
        const string testRoot = @"Software\LemonTests\Startup";
        using var root = Registry.CurrentUser.CreateSubKey(testRoot);
        try
        {
            Assert.False(StartWithWindows.IsEnabled(root));
            StartWithWindows.Enable(@"C:\Apps\Lemon.exe", root);
            Assert.True(StartWithWindows.IsEnabled(root));
            using (var run = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")!)
                Assert.Equal("\"C:\\Apps\\Lemon.exe\" --background", run.GetValue("Lemon"));
            StartWithWindows.Disable(root);
            Assert.False(StartWithWindows.IsEnabled(root));
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(testRoot, throwOnMissingSubKey: false);
        }
    }
}
