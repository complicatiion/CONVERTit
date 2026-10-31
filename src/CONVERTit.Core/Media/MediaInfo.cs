namespace CONVERTit.Core.Media;

/// <summary>Technical details of a file (ffprobe for video/audio, ImageMagick for images).</summary>
public sealed class MediaInfo
{
    public required string Path { get; init; }

    public MediaKind Kind { get; init; }

    public long FileSize { get; init; }

    /// <summary>Container / image format, e.g. "matroska,webm" or "PNG".</summary>
    public string Format { get; init; } = "";

    public double DurationSeconds { get; init; }

    /// <summary>Overall bitrate in bit/s (0 = unknown).</summary>
    public long Bitrate { get; init; }

    public List<VideoStreamInfo> Video { get; init; } = [];

    public List<AudioStreamInfo> Audio { get; init; } = [];

    public List<SubtitleStreamInfo> Subtitles { get; init; } = [];

    /// <summary>Image: width/height, alpha, frame count, color space, bit depth.</summary>
    public ImageDetails? Image { get; init; }

    /// <summary>First real video stream (cover art pictures are skipped).</summary>
    public VideoStreamInfo? MainVideo => Video.FirstOrDefault(v => !v.IsCoverArt);

    public AudioStreamInfo? MainAudio => Audio.FirstOrDefault();

    public VideoStreamInfo? CoverArt => Video.FirstOrDefault(v => v.IsCoverArt);

    public bool HasAlpha => Image?.HasAlpha ?? MainVideo?.HasAlpha ?? false;
}

public sealed class VideoStreamInfo
{
    /// <summary>Absolute stream index in the file (for -map 0:N).</summary>
    public int Index { get; init; }

    public string Codec { get; init; } = "";

    public int Width { get; init; }

    public int Height { get; init; }

    /// <summary>Display rotation in degrees (phone videos).</summary>
    public int Rotation { get; init; }

    public double Fps { get; init; }

    public string PixelFormat { get; init; } = "";

    public long Bitrate { get; init; }

    public bool IsCoverArt { get; init; }

    public bool HasAlpha => PixelFormatHasAlpha(PixelFormat);

    /// <summary>Width/height as shown (rotation 90/270 swaps them).</summary>
    public (int Width, int Height) DisplaySize =>
        Math.Abs(Rotation) % 180 == 90 ? (Height, Width) : (Width, Height);

    public static bool PixelFormatHasAlpha(string? pixelFormat)
    {
        if (string.IsNullOrEmpty(pixelFormat))
        {
            return false;
        }

        var p = pixelFormat.ToLowerInvariant();
        return p.StartsWith("yuva", StringComparison.Ordinal) || p.Contains("rgba", StringComparison.Ordinal)
            || p.Contains("argb", StringComparison.Ordinal) || p.Contains("bgra", StringComparison.Ordinal)
            || p.Contains("abgr", StringComparison.Ordinal) || p.StartsWith("ya", StringComparison.Ordinal)
            || p.StartsWith("gbrap", StringComparison.Ordinal) || p == "pal8";
    }
}

public sealed class AudioStreamInfo
{
    public int Index { get; init; }

    public string Codec { get; init; } = "";

    public int SampleRate { get; init; }

    public int Channels { get; init; }

    public string ChannelLayout { get; init; } = "";

    /// <summary>bit/s, 0 = unknown.</summary>
    public long Bitrate { get; init; }

    public string Language { get; init; } = "";

    /// <summary>Lossless codecs (FLAC, ALAC, PCM, WavPack, APE, TTA).</summary>
    public bool IsLossless =>
        Codec.StartsWith("pcm_", StringComparison.OrdinalIgnoreCase)
        || Codec is "flac" or "alac" or "wavpack" or "ape" or "tta" or "truehd" or "mlp";
}

public sealed class SubtitleStreamInfo
{
    public int Index { get; init; }

    public string Codec { get; init; } = "";

    public string Language { get; init; } = "";

    /// <summary>Picture-based subtitles (Blu-ray PGS, DVD, DVB) cannot be stored in MP4.</summary>
    public bool IsBitmap => Codec is "hdmv_pgs_subtitle" or "dvd_subtitle" or "dvb_subtitle" or "xsub";
}

public sealed class ImageDetails
{
    public int Width { get; init; }

    public int Height { get; init; }

    public bool HasAlpha { get; init; }

    public int Frames { get; init; } = 1;

    public string ColorSpace { get; init; } = "";

    public int BitDepth { get; init; }

    public double DpiX { get; init; }
}
