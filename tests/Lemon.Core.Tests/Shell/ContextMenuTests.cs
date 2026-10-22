using Microsoft.Win32;

namespace Lemon.Core.Tests;

public class ContextMenuTests
{
    [Fact]
    public void RegistersAndUnregistersUnderGivenRoot()
    {
        const string testRoot = @"Software\LemonTests\ContextMenu";
        using var root = Registry.CurrentUser.CreateSubKey(testRoot);
        try
        {
            ContextMenu.Register(@"C:\Apps\Lemon.exe", (from, to) => to != "pdf" && (to != Preset.SameFormat || from == "png"), root);
            Assert.True(ContextMenu.IsRegistered(root));

            using var commands = root.OpenSubKey(@"SystemFileAssociations\.heic\shell\Lemon\shell")!;
            Assert.Equal(["01open", "02topng", "03tojpg", "04towebp"], commands.GetSubKeyNames().Order());
            using var toPng = commands.OpenSubKey(@"02topng\command")!;
            Assert.Equal("\"C:\\Apps\\Lemon.exe\" --to png \"%1\"", toPng.GetValue(""));

            // Files that can be optimized get that right after "Open in Lemon…".
            using (var png = root.OpenSubKey(@"SystemFileAssociations\.png\shell\Lemon\shell")!)
            {
                Assert.Equal(["01open", "02optimize", "03tojpg", "04towebp"], png.GetSubKeyNames().Order());
                using var optimize = png.OpenSubKey(@"02optimize\command")!;
                Assert.Equal("\"C:\\Apps\\Lemon.exe\" --optimize \"%1\"", optimize.GetValue(""));
            }

            // PDF was filtered out by the canConvert callback.
            using (var docx = root.OpenSubKey(@"SystemFileAssociations\.docx\shell\Lemon\shell")!)
                Assert.Equal(["01open", "02tohtml"], docx.GetSubKeyNames().Order());

            // Registering again with fewer possibilities replaces the old entries instead of piling up,
            // and types with nothing to convert to get no menu at all.
            ContextMenu.Register(@"C:\Apps\Lemon.exe", (from, to) => from != "docx" && to == "jpg", root);
            using (var heic = root.OpenSubKey(@"SystemFileAssociations\.heic\shell\Lemon\shell")!)
                Assert.Equal(["01open", "02tojpg"], heic.GetSubKeyNames().Order());
            Assert.Null(root.OpenSubKey(@"SystemFileAssociations\.docx\shell\Lemon"));

            ContextMenu.Unregister(root);
            Assert.False(ContextMenu.IsRegistered(root));
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(testRoot, throwOnMissingSubKey: false);
        }
    }
}
