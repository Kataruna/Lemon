using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using ImageMagick;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Lemon.App.Wheel;

/// <summary>
/// The wheel's frosted glass. Windows' own blur backdrop fills the whole (square) window and could only be cut to a
/// circle by the jagged window region, so instead Lemon blurs a snapshot of what's behind the wheel and paints it
/// inside the anti-aliased disc and mode bar. It's a still picture, taken as the wheel opens.
/// </summary>
internal static partial class Glass
{
    public static readonly string[] Names = ["None", "Light", "Standard", "Heavy"];

    /// <summary>How much of the tinted disc color covers the blurred picture: the glass itself is see-through.</summary>
    public static double TintCover(int blur) => blur switch { <= 0 => 1, 1 => 0.45, 2 => 0.6, _ => 0.7 };

    /// <summary>Blurs a picture of what's behind the wheel. Heavy also frosts it, like glass over frost.</summary>
    /// <param name="scale">Physical pixels per DIP, so the blur looks the same at every display scale.</param>
    public static MagickImage Blur(MagickImage image, int blur, double scale)
    {
        var sigma = (blur switch { 1 => 4, 2 => 10, _ => 18 }) * scale;
        // Blurring a quarter-size copy and scaling it back up is far faster and looks the same at these radii.
        var (width, height) = (image.Width, image.Height);
        image.Resize(new MagickGeometry(Math.Max(1, width / 4), Math.Max(1, height / 4)) { IgnoreAspectRatio = true });
        image.GaussianBlur(0, sigma / 4);
        image.Resize(new MagickGeometry(width, height) { IgnoreAspectRatio = true });
        if (blur >= 3)
            image.Colorize(MagickColors.White, new Percentage(18));
        return image;
    }

    /// <summary>A brush of part of the blurred picture, stretched over an element covering exactly that part.</summary>
    public static Brush Crop(MagickImage image, int x, int y, int width, int height)
    {
        using var part = (MagickImage)image.CloneArea(x, y, (uint)Math.Max(1, width), (uint)Math.Max(1, height));
        var bitmap = new WriteableBitmap((int)part.Width, (int)part.Height);
        var pixels = part.ToByteArray(MagickFormat.Bgra);
        using (var stream = bitmap.PixelBuffer.AsStream())
            stream.Write(pixels);
        bitmap.Invalidate();
        return new ImageBrush { ImageSource = bitmap, Stretch = Stretch.Fill };
    }

    /// <summary>
    /// A picture of the screen in physical pixels. Layered windows (like the drag image under the pointer) are left
    /// out, which is what's wanted here. Null if the screen can't be read.
    /// </summary>
    public static MagickImage? Capture(int x, int y, int width, int height)
    {
        var screen = GetDC(0);
        var memory = CreateCompatibleDC(screen);
        var bitmap = CreateCompatibleBitmap(screen, width, height);
        var old = SelectObject(memory, bitmap);
        try
        {
            if (!BitBlt(memory, 0, 0, width, height, screen, x, y, 0x00CC0020 /* SRCCOPY */))
                return null;
            var info = new BITMAPINFOHEADER
            {
                Size = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                Width = width,
                Height = -height, // top-down rows
                Planes = 1,
                BitCount = 32,
            };
            var pixels = new byte[width * height * 4];
            if (GetDIBits(memory, bitmap, 0, (uint)height, pixels, ref info, 0) == 0)
                return null;
            var image = new MagickImage(pixels, new PixelReadSettings((uint)width, (uint)height, StorageType.Char, PixelMapping.BGRA));
            // GDI leaves the alpha bytes at zero.
            image.Alpha(AlphaOption.Off);
            return image;
        }
        finally
        {
            SelectObject(memory, old);
            Native.DeleteObject(bitmap);
            DeleteDC(memory);
            ReleaseDC(0, screen);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }

    [LibraryImport("user32.dll")]
    private static partial nint GetDC(nint hwnd);

    [LibraryImport("user32.dll")]
    private static partial int ReleaseDC(nint hwnd, nint dc);

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateCompatibleDC(nint dc);

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateCompatibleBitmap(nint dc, int width, int height);

    [LibraryImport("gdi32.dll")]
    private static partial nint SelectObject(nint dc, nint obj);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteDC(nint dc);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool BitBlt(nint dest, int x, int y, int width, int height, nint source, int sourceX, int sourceY, uint rop);

    [LibraryImport("gdi32.dll")]
    private static partial int GetDIBits(nint dc, nint bitmap, uint start, uint lines, [Out] byte[] bits, ref BITMAPINFOHEADER info, uint usage);
}
