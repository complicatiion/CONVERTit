using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CONVERTit.Platform.Windows;

/// <summary>Colors the native title bar (dark/light mode on Windows 10 20H1+, caption color on Windows 11).</summary>
public static class DwmTitleBar
{
    private const int DwmwaUseImmersiveDarkModeOld = 19;
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaBorderColor = 34;
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaTextColor = 36;

    /// <param name="hwnd">Native window handle.</param>
    /// <param name="dark">Dark title bar buttons.</param>
    /// <param name="caption">Caption color as 0xRRGGBB.</param>
    /// <param name="text">Title text color as 0xRRGGBB.</param>
    public static void Apply(IntPtr hwnd, bool dark, int caption, int text)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        try
        {
            var mode = dark ? 1 : 0;
            if (DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref mode, sizeof(int)) != 0)
            {
                DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkModeOld, ref mode, sizeof(int));
            }

            // Ignored before Windows 11 (returns an error code, no exception).
            var captionRef = ToColorRef(caption);
            var textRef = ToColorRef(text);
            DwmSetWindowAttribute(hwnd, DwmwaCaptionColor, ref captionRef, sizeof(int));
            DwmSetWindowAttribute(hwnd, DwmwaBorderColor, ref captionRef, sizeof(int));
            DwmSetWindowAttribute(hwnd, DwmwaTextColor, ref textRef, sizeof(int));
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }
    }

    // COLORREF is 0x00BBGGRR.
    private static int ToColorRef(int rgb) =>
        ((rgb & 0xFF) << 16) | (rgb & 0xFF00) | ((rgb >> 16) & 0xFF);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}

/// <summary>Keeps the PC awake while a batch runs (the display may still turn off).</summary>
public static class PowerRequest
{
    private const uint EsContinuous = 0x80000000;
    private const uint EsSystemRequired = 0x00000001;

    /// <summary>The state belongs to the calling thread: always call from the UI thread.</summary>
    public static void Set(bool keepAwake)
    {
        try
        {
            SetThreadExecutionState(keepAwake ? EsContinuous | EsSystemRequired : EsContinuous);
        }
        catch (EntryPointNotFoundException)
        {
        }
    }

    [DllImport("kernel32.dll")]
    private static extern uint SetThreadExecutionState(uint esFlags);
}

/// <summary>Explorer and shell helpers.</summary>
public static class Shell
{
    /// <summary>Opens Explorer with the file selected.</summary>
    public static void SelectInExplorer(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                Process.Start(new ProcessStartInfo("explorer.exe") { Arguments = "/select,\"" + path + "\"", UseShellExecute = false });
            }
            else
            {
                var dir = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                {
                    Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
                }
            }
        }
        catch
        {
            // Explorer not available.
        }
    }

    /// <summary>Sets the AppUserModelID of this process (taskbar grouping, notifications).</summary>
    public static void SetAppId(string appId)
    {
        try
        {
            SetCurrentProcessExplicitAppUserModelID(appId);
        }
        catch (EntryPointNotFoundException)
        {
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SetCurrentProcessExplicitAppUserModelID(string appId);
}
