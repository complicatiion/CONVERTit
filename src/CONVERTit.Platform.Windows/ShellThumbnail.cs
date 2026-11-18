using System.Runtime.InteropServices;

namespace CONVERTit.Platform.Windows;

/// <summary>Thumbnail pixels from Windows: 32-bit BGRA, premultiplied, top-down rows.</summary>
public sealed record ShellBitmap(int Width, int Height, byte[] Pixels)
{
    public int Stride => Width * 4;
}

/// <summary>
/// Thumbnails from the Windows thumbnail cache (IShellItemImageFactory, the same pictures Explorer shows). Fast
/// and knows every format Windows has a thumbnail handler for, including videos with installed codecs. Only real
/// thumbnails are returned (no file type icons); null when Windows has none.
/// </summary>
public static class ShellThumbnail
{
    private static readonly Guid IidShellItemImageFactory = new("bcc18b79-ba16-442f-80c4-8a59c30c463b");

    private const int SiigbfBiggerSizeOk = 0x1;
    private const int SiigbfThumbnailOnly = 0x8;

    public static unsafe ShellBitmap? Get(string path, int size)
    {
        IntPtr factory = IntPtr.Zero;
        IntPtr bitmap = IntPtr.Zero;
        try
        {
            var iid = IidShellItemImageFactory;
            if (SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out factory) < 0 || factory == IntPtr.Zero)
            {
                return null;
            }

            // IShellItemImageFactory::GetImage = vtable slot 3 (after IUnknown).
            var vtbl = *(IntPtr**)factory;
            var getImage = (delegate* unmanaged[Stdcall]<IntPtr, Size, int, IntPtr*, int>)vtbl[3];
            IntPtr hbitmap;
            if (getImage(factory, new Size(size, size), SiigbfThumbnailOnly | SiigbfBiggerSizeOk, &hbitmap) < 0 || hbitmap == IntPtr.Zero)
            {
                return null;
            }

            bitmap = hbitmap;
            return Read(hbitmap);
        }
        catch (Exception ex) when (ex is COMException or EntryPointNotFoundException or DllNotFoundException or SEHException)
        {
            return null;
        }
        finally
        {
            if (bitmap != IntPtr.Zero)
            {
                DeleteObject(bitmap);
            }

            if (factory != IntPtr.Zero)
            {
                Marshal.Release(factory);
            }
        }
    }

    /// <summary>HBITMAP → BGRA bytes (top-down). Bitmaps without alpha (all 0) get opaque alpha.</summary>
    private static ShellBitmap? Read(IntPtr hbitmap)
    {
        if (GetObject(hbitmap, Marshal.SizeOf<BitmapInfoObject>(), out var info) == 0 || info.Width <= 0 || info.Height == 0)
        {
            return null;
        }

        var width = info.Width;
        var height = Math.Abs(info.Height);
        var header = new BitmapInfoHeader
        {
            Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
            Width = width,
            Height = -height, // top-down
            Planes = 1,
            BitCount = 32,
            Compression = 0
        };
        var pixels = new byte[width * height * 4];
        var dc = CreateCompatibleDC(IntPtr.Zero);
        try
        {
            if (GetDIBits(dc, hbitmap, 0, (uint)height, pixels, ref header, 0) == 0)
            {
                return null;
            }
        }
        finally
        {
            DeleteDC(dc);
        }

        var anyAlpha = false;
        for (var i = 3; i < pixels.Length; i += 4)
        {
            if (pixels[i] != 0)
            {
                anyAlpha = true;
                break;
            }
        }

        if (!anyAlpha)
        {
            for (var i = 3; i < pixels.Length; i += 4)
            {
                pixels[i] = 255;
            }
        }

        return new ShellBitmap(width, height, pixels);
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct Size(int cx, int cy)
    {
        public readonly int Cx = cx;
        public readonly int Cy = cy;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoObject
    {
        public int Type;
        public int Width;
        public int Height;
        public int WidthBytes;
        public ushort Planes;
        public ushort BitsPixel;
        public IntPtr Bits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
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

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(string path, IntPtr bindContext, ref Guid riid, out IntPtr item);

    [DllImport("gdi32.dll")]
    private static extern int GetObject(IntPtr handle, int size, out BitmapInfoObject info);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint lines, byte[] bits, ref BitmapInfoHeader info, uint usage);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr handle);
}
