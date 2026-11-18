using System.Runtime.InteropServices;

namespace CONVERTit.Platform.Windows;

/// <summary>
/// Puts an image on the Windows clipboard in two formats: CF_DIB (every program) and "PNG" (keeps transparency, read
/// by browsers, Office, GIMP, Paint.NET …).
/// </summary>
public static class ClipboardImage
{
    private const uint CfDib = 8;
    private const uint GmemMoveable = 0x0002;

    /// <param name="bmpFile">A complete .bmp file (the 14-byte file header is stripped for CF_DIB).</param>
    /// <param name="png">PNG bytes or null.</param>
    public static bool Set(byte[] bmpFile, byte[]? png)
    {
        if (!OperatingSystem.IsWindows() || bmpFile.Length <= 14)
        {
            return false;
        }

        for (var attempt = 0; attempt < 10; attempt++)
        {
            if (OpenClipboard(IntPtr.Zero))
            {
                try
                {
                    EmptyClipboard();
                    var ok = Put(CfDib, bmpFile.AsSpan(14));
                    if (png is { Length: > 0 })
                    {
                        var pngFormat = RegisterClipboardFormat("PNG");
                        if (pngFormat != 0)
                        {
                            Put(pngFormat, png);
                        }
                    }

                    return ok;
                }
                finally
                {
                    CloseClipboard();
                }
            }

            // Another program holds the clipboard for a moment.
            Thread.Sleep(50);
        }

        return false;
    }

    private static bool Put(uint format, ReadOnlySpan<byte> data)
    {
        var handle = GlobalAlloc(GmemMoveable, (nuint)data.Length);
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        var pointer = GlobalLock(handle);
        if (pointer == IntPtr.Zero)
        {
            GlobalFree(handle);
            return false;
        }

        Marshal.Copy(data.ToArray(), 0, pointer, data.Length);
        GlobalUnlock(handle);
        if (SetClipboardData(format, handle) == IntPtr.Zero)
        {
            GlobalFree(handle);
            return false;
        }

        // The clipboard owns the memory now.
        return true;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr owner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint format, IntPtr handle);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint RegisterClipboardFormat(string name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint flags, nuint bytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalUnlock(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalFree(IntPtr handle);
}
