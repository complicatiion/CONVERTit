namespace CONVERTit.Engines.Ffmpeg;

public enum FfmpegSource { Missing, Custom, Downloaded, AppFolder, SystemPath }

/// <summary>
/// Finds ffmpeg/ffprobe: custom path from the settings, the downloaded copy in the data folder,
/// a copy next to the app (bin\ffmpeg) or the PATH. FFmpeg is never bundled with CONVERTit.
/// </summary>
public sealed class FfmpegLocator(string toolsDirectory, Func<string?> customPath)
{
    private static readonly string Exe = OperatingSystem.IsWindows() ? ".exe" : "";

    /// <summary>Folder for the downloaded copy (data\tools\ffmpeg).</summary>
    public string ToolsDirectory { get; } = toolsDirectory;

    public string? FfmpegPath { get; private set; }

    public string? FfprobePath { get; private set; }

    public FfmpegSource Source { get; private set; }

    public bool IsAvailable => FfmpegPath is not null && FfprobePath is not null;

    public event Action? Changed;

    public void Refresh()
    {
        (FfmpegPath, FfprobePath, Source) = Find();
        Changed?.Invoke();
    }

    private (string?, string?, FfmpegSource) Find()
    {
        var custom = customPath()?.Trim().Trim('"');
        if (!string.IsNullOrEmpty(custom))
        {
            var dir = Directory.Exists(custom) ? custom : Path.GetDirectoryName(custom);
            if (TryFolder(dir, out var f, out var p))
            {
                return (f, p, FfmpegSource.Custom);
            }
        }

        if (TryFolder(ToolsDirectory, out var downloadedFfmpeg, out var downloadedProbe))
        {
            return (downloadedFfmpeg, downloadedProbe, FfmpegSource.Downloaded);
        }

        foreach (var dir in new[] { Path.Combine(AppContext.BaseDirectory, "bin", "ffmpeg"), Path.Combine(AppContext.BaseDirectory, "ffmpeg") })
        {
            if (TryFolder(dir, out var f, out var p))
            {
                return (f, p, FfmpegSource.AppFolder);
            }
        }

        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            if (TryFolder(dir.Trim().Trim('"'), out var f, out var p))
            {
                return (f, p, FfmpegSource.SystemPath);
            }
        }

        return (null, null, FfmpegSource.Missing);
    }

    private static bool TryFolder(string? dir, out string ffmpeg, out string ffprobe)
    {
        ffmpeg = ffprobe = "";
        if (string.IsNullOrEmpty(dir))
        {
            return false;
        }

        try
        {
            ffmpeg = Path.Combine(dir, "ffmpeg" + Exe);
            ffprobe = Path.Combine(dir, "ffprobe" + Exe);
            return File.Exists(ffmpeg) && File.Exists(ffprobe);
        }
        catch
        {
            return false;
        }
    }
}
