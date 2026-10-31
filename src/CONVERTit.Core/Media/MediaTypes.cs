namespace CONVERTit.Core.Media;

public enum MediaKind { Unknown, Video, Audio, Image }

/// <summary>File kind by extension (fast, used for lists and folder batches).</summary>
public static class MediaTypes
{
    public static readonly IReadOnlySet<string> VideoExtensions = Set(
        "mp4", "m4v", "mkv", "webm", "mov", "avi", "wmv", "flv", "mpg", "mpeg", "m2v", "ts", "m2ts", "mts",
        "3gp", "3g2", "ogv", "vob", "mxf", "asf", "f4v", "divx", "y4m");

    public static readonly IReadOnlySet<string> AudioExtensions = Set(
        "mp3", "aac", "m4a", "m4b", "opus", "ogg", "oga", "flac", "wav", "wave", "wma", "aif", "aiff", "aifc",
        "ape", "wv", "ac3", "eac3", "dts", "mka", "amr", "mp2", "mpc", "caf", "tta", "spx", "au");

    public static readonly IReadOnlySet<string> ImageExtensions = Set(
        "png", "jpg", "jpeg", "jpe", "jfif", "webp", "avif", "heic", "heif", "tif", "tiff", "bmp", "dib", "gif",
        "ico", "psd", "tga", "svg", "jxl", "jp2", "dds", "pcx", "ppm", "pgm", "pbm", "exr", "hdr", "qoi");

    public static string Extension(string path) =>
        Path.GetExtension(path).TrimStart('.').ToLowerInvariant();

    public static MediaKind Detect(string path)
    {
        var ext = Extension(path);
        if (VideoExtensions.Contains(ext)) return MediaKind.Video;
        if (AudioExtensions.Contains(ext)) return MediaKind.Audio;
        if (ImageExtensions.Contains(ext)) return MediaKind.Image;
        return MediaKind.Unknown;
    }

    public static IReadOnlySet<string> ExtensionsOf(MediaKind kind) => kind switch
    {
        MediaKind.Video => VideoExtensions,
        MediaKind.Audio => AudioExtensions,
        MediaKind.Image => ImageExtensions,
        _ => new HashSet<string>()
    };

    /// <summary>"mp3, .OGG m4a;flac" → {mp3, ogg, m4a, flac}.</summary>
    public static HashSet<string> ParseExtensionList(string? text)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(text))
        {
            return result;
        }

        foreach (var part in text.Split([',', ';', ' ', '\t', '|'], StringSplitOptions.RemoveEmptyEntries))
        {
            var ext = part.Trim().TrimStart('*').TrimStart('.').ToLowerInvariant();
            if (ext.Length > 0)
            {
                result.Add(ext);
            }
        }

        return result;
    }

    private static HashSet<string> Set(params string[] values) => new(values, StringComparer.OrdinalIgnoreCase);
}
