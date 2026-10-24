#:package Magick.NET-Q8-AnyCPU@14.17.2

// Makes the Windows app icon from the Mac one (mac/Lemon.png), so both apps share it.
// Usage: dotnet run make-icon.cs [-- <output folder>]; by default it writes to src/Lemon.App/Assets.
using ImageMagick;

var here = (string)AppContext.GetData("EntryPointFileDirectoryPath")!;
var outDir = args.Length > 0 ? args[0] : Path.Combine(here, "..", "src", "Lemon.App", "Assets");
Directory.CreateDirectory(outDir);

using var master = new MagickImage(Path.Combine(here, "..", "..", "mac", "Lemon.png"));
master.Trim();
master.BackgroundColor = MagickColors.Transparent;
master.Extent(Math.Max(master.Width, master.Height), Math.Max(master.Width, master.Height), Gravity.Center);
master.Resize(256, 256);
master.Write(Path.Combine(outDir, "Lemon.png"), MagickFormat.Png);

using var icon = new MagickImageCollection();
foreach (var size in new uint[] { 256, 64, 48, 32, 24, 16 })
{
    var frame = master.CloneAndMutate(i => i.Resize(size, size));
    icon.Add(frame);
}
icon.Write(Path.Combine(outDir, "Lemon.ico"), MagickFormat.Ico);
Console.WriteLine($"Wrote icon to {Path.GetFullPath(outDir)}");
