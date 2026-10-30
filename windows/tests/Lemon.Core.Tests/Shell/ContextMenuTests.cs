using Lemon.Core.Models;
using Microsoft.Win32;

namespace Lemon.Core.Tests;

public class ContextMenuTests
{
    private static bool NoModels(string extension, Preset preset) => false;

    [Fact]
    public void OffersAiActionsForDownloadedModelsWithLinesBetweenGroups()
    {
        const string testRoot = @"Software\LemonTests\ContextMenuAi";
        using var root = Registry.CurrentUser.CreateSubKey(testRoot);
        try
        {
            // Only the full background model and the lite upscale model are downloaded, and GIFs can't be upscaled.
            ContextMenu.Register(
                @"C:\Apps\Lemon.exe",
                (from, to) => to is "jpg" or Preset.SameFormat,
                (from, p) => p.Id == "img-nobg-full" || (p.Upscale == UpscaleModel.Lite && from != "gif"),
                root);

            using var png = root.OpenSubKey(@"SystemFileAssociations\.png\shell\Lemon\shell")!;
            Assert.Equal(["01open", "02optimize", "03nobg", "04upscale2", "05upscale4", "06tojpg"], png.GetSubKeyNames().Order());
            using (var nobg = png.OpenSubKey(@"03nobg\command")!)
                Assert.Equal("\"C:\\Apps\\Lemon.exe\" --preset img-nobg-full \"%1\"", nobg.GetValue(""));

            // A line above the first entry of each group, none inside a group.
            Assert.Equal(["02optimize", "06tojpg"], png.GetSubKeyNames().Where(HasLine).Order());
            bool HasLine(string name)
            {
                using var item = png.OpenSubKey(name)!;
                return item.GetValue("CommandFlags") is 0x20;
            }

            using var gif = root.OpenSubKey(@"SystemFileAssociations\.gif\shell\Lemon\shell")!;
            Assert.DoesNotContain(gif.GetSubKeyNames(), name => name.Contains("upscale"));
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(testRoot, throwOnMissingSubKey: false);
        }
    }

    [Fact]
    public void RegistersAndUnregistersUnderGivenRoot()
    {
        const string testRoot = @"Software\LemonTests\ContextMenu";
        using var root = Registry.CurrentUser.CreateSubKey(testRoot);
        try
        {
            ContextMenu.Register(@"C:\Apps\Lemon.exe", (from, to) => to != "pdf" && (to != Preset.SameFormat || from == "png"), NoModels, root);
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
            ContextMenu.Register(@"C:\Apps\Lemon.exe", (from, to) => from != "docx" && to == "jpg", NoModels, root);
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
