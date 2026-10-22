#:package Magick.NET-Q8-AnyCPU@14.17.2

// Draws the Lemon app icon. Usage: dotnet run make-icon.cs -- src/Lemon.App/Assets
using ImageMagick;
using ImageMagick.Drawing;

var outDir = args.Length > 0 ? args[0] : ".";
Directory.CreateDirectory(outDir);

using var master = new MagickImage(MagickColors.Transparent, 512, 512);
new Drawables()
    // Leaf
    .FillColor(new MagickColor("#3FA34D"))
    .Ellipse(352, 104, 70, 30, 0, 360)
    // Lemon body, slightly rotated with pointed tips
    .Translation(256, 276)
    .Rotation(-30)
    .FillColor(new MagickColor("#E8B100"))
    .Ellipse(0, 0, 200, 156, 0, 360)
    .FillColor(new MagickColor("#FFD43B"))
    .Ellipse(-6, -8, 186, 142, 0, 360)
    .Ellipse(-190, 0, 30, 22, 0, 360)
    .Ellipse(190, 0, 30, 22, 0, 360)
    // Highlight
    .FillColor(new MagickColor("#FFF3B0"))
    .Ellipse(-70, -70, 70, 26, 0, 360)
    .Draw(master);

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
