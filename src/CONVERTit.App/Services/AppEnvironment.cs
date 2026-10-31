using System.Diagnostics;
using System.Reflection;

namespace CONVERTit.App.Services;

/// <summary>Paths, version and shell helpers.</summary>
public static class AppEnvironment
{
    public const string ProjectUrl = "https://github.com/complicatiion/CONVERTit";
    public const string ReleasesUrl = "https://github.com/complicatiion/CONVERTit/releases";
    public const string WebsiteUrl = "https://sksdesign.de";
    public const string MitLicenseUrl = "https://opensource.org/license/mit";

    public static string Version { get; } =
        typeof(AppEnvironment).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0]
        ?? "0.0.5";

    /// <summary>Portable mode: marker file CONVERTit.portable next to the app, data in .\data.</summary>
    public static bool IsPortable { get; } = File.Exists(Path.Combine(AppContext.BaseDirectory, "CONVERTit.portable"));

    public static string DataDirectory { get; } = IsPortable
        ? Path.Combine(AppContext.BaseDirectory, "data")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CONVERTit");

    /// <summary>Downloaded FFmpeg (ffmpeg.exe, ffprobe.exe, LICENSE.txt, download.json).</summary>
    public static string FfmpegDirectory => Path.Combine(DataDirectory, "tools", "ffmpeg");

    public static string SettingsPath => Path.Combine(DataDirectory, "settings.json");

    public static string LicensesDirectory => Path.Combine(AppContext.BaseDirectory, "licenses");

    public static string ExecutablePath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "CONVERTit.exe");

    public static string NotificationIconPath => Path.Combine(AppContext.BaseDirectory, "convertit.png");

    public static void OpenUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !(url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // No browser.
        }
    }

    public static void OpenFolder(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch
        {
            // Not available.
        }
    }

    /// <summary>Explorer with the file selected (folder when the file is gone).</summary>
    public static void ShowInExplorer(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            Platform.Windows.Shell.SelectInExplorer(path);
        }
        else
        {
            OpenFolder(File.Exists(path) ? Path.GetDirectoryName(path) : path);
        }
    }

    /// <summary>Opens a file with its default program.</summary>
    public static void OpenFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch
        {
            // No associated program.
        }
    }
}
