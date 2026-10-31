using CONVERTit.Core.Catalog;
using CONVERTit.Core.Media;
using CONVERTit.Core.Options;

namespace CONVERTit.Core.Ffmpeg;

/// <summary>
/// Turns the compress options into concrete encoder settings per file: video bitrate for a target size,
/// audio bitrate steps and image options. Pure functions (unit-tested).
/// </summary>
public static class CompressPlanner
{
    /// <summary>Muxer overhead reserve: the encoders aim slightly below the target.</summary>
    public const double ContainerReserve = 0.96;

    /// <summary>Lowest video bitrate that still makes sense (kbps).</summary>
    public const int MinVideoKbps = 80;

    /// <summary>Target size in bytes for a source file; null in quality mode.</summary>
    public static long? TargetBytes(long sourceBytes, CompressOptions o) => o.Mode switch
    {
        CompressMode.TargetSize => Math.Max(1, o.TargetMb) * 1024L * 1024L,
        CompressMode.Percent when sourceBytes > 0 => (long)(sourceBytes * Math.Clamp(o.Percent, 1, 99) / 100d),
        _ => null
    };

    /// <summary>CRF for the quality mode (lower = better).</summary>
    public static int CrfFor(VideoCodec codec, CompressStrength strength) => (codec, strength) switch
    {
        (VideoCodec.H264, CompressStrength.Light) => 22,
        (VideoCodec.H264, CompressStrength.Medium) => 26,
        (VideoCodec.H264, CompressStrength.Strong) => 30,
        (VideoCodec.H265, CompressStrength.Light) => 24,
        (VideoCodec.H265, CompressStrength.Medium) => 28,
        (VideoCodec.H265, CompressStrength.Strong) => 32,
        (_, CompressStrength.Light) => 30,
        (_, CompressStrength.Medium) => 36,
        _ => 42
    };

    /// <summary>Video bitrate (kbps) that fills <paramref name="targetBytes"/> together with the audio.</summary>
    public static int VideoKbpsForSize(double seconds, long targetBytes, int audioKbps)
    {
        if (seconds <= 0 || targetBytes <= 0)
        {
            return 0;
        }

        var totalKbps = targetBytes * 8d / 1000d / seconds * ContainerReserve;
        return (int)Math.Max(MinVideoKbps, Math.Floor(totalKbps - Math.Max(0, audioKbps)));
    }

    /// <summary>Encoder settings for one video (always MP4).</summary>
    public static VideoOptions Video(MediaInfo info, CompressOptions o, List<string> warnings)
    {
        var codec = FormatCatalog.CodecsFor(VideoContainer.Mp4).Contains(o.Codec) ? o.Codec : VideoCodec.H264;
        var audioKbps = info.Audio.Count == 0 ? 0 : Math.Max(0, o.VideoAudioKbps);
        var v = new VideoOptions
        {
            Container = VideoContainer.Mp4,
            Codec = codec,
            Acceleration = o.Acceleration,
            Resolution = o.MaxResolution is ResolutionPreset.Custom ? ResolutionPreset.Original : o.MaxResolution,
            NoUpscale = true,
            FrameRate = o.MaxFrameRate is FrameRatePreset.Custom ? FrameRatePreset.Original : o.MaxFrameRate,
            FpsConversion = FpsConversion.Drop,
            Audio = audioKbps > 0 ? VideoAudioMode.Aac : VideoAudioMode.Remove,
            AudioBitrateKbps = Math.Clamp(audioKbps, 32, 320),
            KeepSubtitles = true,
            KeepMetadata = true,
            FastStart = true,
            Compatible8Bit = true,
            Speed = EncoderSpeed.Balanced
        };

        // Frame rate limit: never raise the frame rate.
        var fps = FormatCatalog.FrameRateValue(v.FrameRate, 0);
        if (fps is { } limit && info.MainVideo is { Fps: > 0 } mv && mv.Fps <= limit + 0.01)
        {
            v.FrameRate = FrameRatePreset.Original;
        }

        var target = TargetBytes(info.FileSize, o);
        if (target is null)
        {
            v.QualityMode = VideoQualityMode.Crf;
            v.Crf = CrfFor(codec, o.Strength);
            return v;
        }

        var kbps = VideoKbpsForSize(info.DurationSeconds, target.Value, audioKbps);
        if (kbps <= 0)
        {
            throw new ConversionException("ErrNoDuration");
        }

        if (kbps <= MinVideoKbps)
        {
            warnings.Add("WarnTargetTooSmall");
        }

        // A target above the source bitrate would only make the file bigger.
        var sourceVideoBits = info.MainVideo?.Bitrate > 0
            ? info.MainVideo.Bitrate
            : info.Bitrate > 0 ? info.Bitrate - info.Audio.Sum(a => a.Bitrate) : 0;
        if (sourceVideoBits > 0)
        {
            var cap = (int)(sourceVideoBits / 1000d * 0.9);
            if (cap >= MinVideoKbps && kbps > cap)
            {
                kbps = cap;
                warnings.Add("WarnTargetAboveSource");
            }
        }

        v.QualityMode = VideoQualityMode.Bitrate;
        v.BitrateKbps = kbps;
        return v;
    }

    /// <summary>
    /// Video › Convert in "target size" mode: the share of the source size becomes a video bitrate for this file
    /// (source average bitrate × percent, minus the audio). Returns a copy in bitrate mode.
    /// </summary>
    public static VideoOptions ResolveTargetSize(MediaInfo info, VideoOptions o)
    {
        var resolved = Storage.JsonFile.Clone(o);
        resolved.QualityMode = VideoQualityMode.Bitrate;
        var sourceKbps = info.Bitrate > 0
            ? info.Bitrate / 1000d
            : info.DurationSeconds > 0 ? info.FileSize * 8d / 1000d / info.DurationSeconds : 0;
        if (sourceKbps <= 0)
        {
            resolved.QualityMode = VideoQualityMode.Preset;
            return resolved;
        }

        double audioKbps = 0;
        if (o.Audio != VideoAudioMode.Remove && info.Audio.Count > 0)
        {
            var copy = o.Audio == VideoAudioMode.Copy || (o.Audio == VideoAudioMode.Auto
                && info.Audio.All(t => FormatCatalog.AudioCopyAllowed(o.Container, t.Codec)));
            audioKbps = copy
                ? info.Audio.Sum(t => t.Bitrate > 0 ? t.Bitrate / 1000d : 160)
                : info.Audio.Count * Math.Clamp(o.AudioBitrateKbps, 32, 512);
        }

        var target = sourceKbps * Math.Clamp(o.TargetPercent, 5, 100) / 100d * ContainerReserve;
        resolved.BitrateKbps = (int)Math.Max(MinVideoKbps, Math.Floor(target - audioKbps));
        return resolved;
    }

    /// <summary>Encoder settings for one audio file.</summary>
    public static AudioOptions Audio(MediaInfo info, CompressOptions o)
    {
        var format = o.AudioFormat is AudioFormat.Mp3 or AudioFormat.Aac or AudioFormat.Opus ? o.AudioFormat : AudioFormat.Mp3;
        var steps = FormatCatalog.BitratesFor(format);
        var kbps = o.AudioKbps;
        var target = TargetBytes(info.FileSize, o);
        if (target is { } bytes && info.DurationSeconds > 0)
        {
            kbps = (int)(bytes * 8d / 1000d / info.DurationSeconds * ContainerReserve);
        }

        // Snap to the next lower standard step (LAME and AAC work best with these).
        var snapped = steps.Where(s => s <= kbps).DefaultIfEmpty(steps[^1]).Max();
        return new AudioOptions
        {
            Format = format,
            BitrateKbps = snapped,
            Mp3Mode = BitrateMode.Cbr,
            KeepMetadata = !o.StripMetadata,
            KeepCover = !o.StripMetadata,
            NotAboveSource = true
        };
    }

    /// <summary>Image settings; the "Keep" format is resolved by the image engine.</summary>
    public static ImageOptions Image(CompressOptions o)
    {
        var format = o.ImageFormat switch
        {
            CompressImageFormat.Jpeg => ImageFormat.Jpeg,
            CompressImageFormat.WebP => ImageFormat.WebP,
            CompressImageFormat.Avif => ImageFormat.Avif,
            _ => ImageFormat.Keep
        };
        return new ImageOptions
        {
            Format = format,
            Quality = Math.Clamp(o.ImageQuality, 1, 100),
            PngCompression = 9,
            PngPalette = false,
            KeepTransparency = true,
            JpegProgressive = true,
            WebpMethod = 6,
            AvifSpeed = 6,
            Resize = o.ImageMaxSide > 0 ? ResizeMode.MaxSize : ResizeMode.Off,
            Width = o.ImageMaxSide,
            Height = o.ImageMaxSide,
            NoUpscale = true,
            AutoOrient = true,
            StripMetadata = o.StripMetadata,
            KeepAnimation = true
        };
    }

    /// <summary>Expected output size (for the footer estimate).</summary>
    public static long? Estimate(MediaInfo info, CompressOptions o)
    {
        if (info.Kind == MediaKind.Image)
        {
            return null;
        }

        var target = TargetBytes(info.FileSize, o);
        if (target is not null)
        {
            return Math.Min(target.Value, info.FileSize);
        }

        if (info.Kind == MediaKind.Audio)
        {
            return info.DurationSeconds > 0 ? (long)(Audio(info, o).BitrateKbps * 1000d * info.DurationSeconds / 8) : null;
        }

        var video = Video(info, o, []);
        return Estimation.SizeEstimator.Video(info, video);
    }
}
