using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using CONVERTit.Platform.Windows;

namespace CONVERTit.App.Services;

/// <summary>Native title bar in the colors of the current theme (follows theme switches).</summary>
public static class WindowChrome
{
    public static void Apply(Window window)
    {
        window.Opened += (_, _) => Update(window);
        window.ActualThemeVariantChanged += (_, _) => Update(window);
    }

    private static void Update(Window window)
    {
        if (!OperatingSystem.IsWindows() || window.TryGetPlatformHandle()?.Handle is not { } hwnd)
        {
            return;
        }

        var dark = window.ActualThemeVariant != ThemeVariant.Light;
        // Dark: BrushBgSidebar #131217 / text #F4F4F2 · Light: BrushBgSidebar #ECEDF1 / #17171C.
        DwmTitleBar.Apply(hwnd, dark, dark ? 0x131217 : 0xECEDF1, dark ? 0xF4F4F2 : 0x17171C);
    }
}
