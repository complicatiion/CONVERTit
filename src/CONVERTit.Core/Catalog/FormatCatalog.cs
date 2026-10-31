using CONVERTit.Core.Options;

namespace CONVERTit.Core.Catalog;

/// <summary>
/// What each container, codec and format supports: extensions, FFmpeg encoder names, sensible quality
/// levels and container/codec compatibility. Pure data, no I/O.
/// </summary>
public static class FormatCatalog
{
    // ================================================================ Video containers

    public static string Extension(VideoContainer container) => container switch
    {
        VideoContainer.Mkv => "mkv",
        VideoContainer.WebM => "webm",
        VideoContainer.Mov => "mov",
        VideoContainer.Avi => "avi",
        _ => "mp4"
    };

    public static VideoContainer? ContainerFromExtension(string? ext) => ext?.TrimStart('.').ToLowerInvariant() switch
    {
        "mp4" or "m4v" => VideoContainer.Mp4,
        "mkv" => VideoContainer.Mkv,
        "webm" => VideoContainer.WebM,
        "mov" => VideoContainer.Mov,
        "avi" => VideoContainer.Avi,
        _ => null
    };

    /// <summary>Video codecs a container can hold.</summary>
    public static IReadOnlyList<VideoCodec> CodecsFor(VideoContainer container) => container switch
    {
        VideoContainer.WebM => [VideoCodec.Vp9, VideoCodec.Av1],
        VideoContainer.Mov => [VideoCodec.H264, VideoCodec.H265],
        VideoContainer.Avi => [VideoCodec.H264],
        VideoContainer.Mp4 => [VideoCodec.H264, VideoCodec.H265, VideoCodec.Av1, VideoCodec.Vp9],
        _ => [VideoCodec.H264, VideoCodec.H265, VideoCodec.Av1, VideoCodec.Vp9]
    };

    /// <summary>Audio codecs (FFmpeg codec names) a container accepts as stream copy.</summary>
    public static bool AudioCopyAllowed(VideoContainer container, string? codec)
    {
        if (string.IsNullOrEmpty(codec))
        {
            return false;
        }

        return container switch
        {
            VideoContainer.Mkv => true,
            VideoContainer.WebM => codec is "opus" or "vorbis",
            VideoContainer.Mp4 => codec is "aac" or "mp3" or "ac3" or "eac3" or "opus" or "alac" or "flac",
            VideoContainer.Mov => codec is "aac" or "mp3" or "ac3" or "alac" || codec.StartsWith("pcm_", StringComparison.Ordinal),
            VideoContainer.Avi => codec is "mp3" or "ac3" or "pcm_s16le" or "mp2",
            _ => false
        };
    }

    /// <summary>Audio codec chosen by <see cref="VideoAudioMode.Auto"/> when the source track cannot be copied.</summary>
    public static VideoAudioMode DefaultAudioFor(VideoContainer container) => container switch
    {
        VideoContainer.WebM => VideoAudioMode.Opus,
        VideoContainer.Avi => VideoAudioMode.Mp3,
        _ => VideoAudioMode.Aac
    };

    public static IReadOnlyList<VideoAudioMode> AudioModesFor(VideoContainer container) => container switch
    {
        VideoContainer.WebM => [VideoAudioMode.Auto, VideoAudioMode.Copy, VideoAudioMode.Opus, VideoAudioMode.Remove],
        VideoContainer.Avi => [VideoAudioMode.Auto, VideoAudioMode.Copy, VideoAudioMode.Mp3, VideoAudioMode.Remove],
        VideoContainer.Mov => [VideoAudioMode.Auto, VideoAudioMode.Copy, VideoAudioMode.Aac, VideoAudioMode.Mp3, VideoAudioMode.Remove],
        _ => [VideoAudioMode.Auto, VideoAudioMode.Copy, VideoAudioMode.Aac, VideoAudioMode.Opus, VideoAudioMode.Mp3, VideoAudioMode.Remove]
    };

    public static VideoCodec? CodecFromFfmpeg(string? codec) => codec switch
    {
        "h264" => VideoCodec.H264,
        "hevc" => VideoCodec.H265,
        "av1" => VideoCodec.Av1,
        "vp9" => VideoCodec.Vp9,
        _ => null
    };

    // ================================================================ Video encoders

    /// <summary>FFmpeg encoder for codec + hardware family; null when the combination does not exist.</summary>
    public static string? VideoEncoder(VideoCodec codec, Acceleration acceleration) => (codec, acceleration) switch
    {
        (VideoCodec.H264, Acceleration.Off) => "libx264",
        (VideoCodec.H264, Acceleration.Nvidia) => "h264_nvenc",
        (VideoCodec.H264, Acceleration.Intel) => "h264_qsv",
        (VideoCodec.H264, Acceleration.Amd) => "h264_amf",
        (VideoCodec.H265, Acceleration.Off) => "libx265",
        (VideoCodec.H265, Acceleration.Nvidia) => "hevc_nvenc",
        (VideoCodec.H265, Acceleration.Intel) => "hevc_qsv",
        (VideoCodec.H265, Acceleration.Amd) => "hevc_amf",
        (VideoCodec.Av1, Acceleration.Off) => "libsvtav1",
        (VideoCodec.Av1, Acceleration.Nvidia) => "av1_nvenc",
        (VideoCodec.Av1, Acceleration.Intel) => "av1_qsv",
        (VideoCodec.Av1, Acceleration.Amd) => "av1_amf",
        (VideoCodec.Vp9, Acceleration.Off) => "libvpx-vp9",
        _ => null
    };

    /// <summary>Hardware encoders CONVERTit tests on start (see HardwareProbe).</summary>
    public static readonly IReadOnlyList<string> HardwareEncoders =
    [
        "h264_nvenc", "hevc_nvenc", "av1_nvenc",
        "h264_qsv", "hevc_qsv", "av1_qsv",
        "h264_amf", "hevc_amf", "av1_amf"
    ];

    public static Acceleration AccelerationOf(string encoder) =>
        encoder.EndsWith("_nvenc", StringComparison.Ordinal) ? Acceleration.Nvidia
        : encoder.EndsWith("_qsv", StringComparison.Ordinal) ? Acceleration.Intel
        : encoder.EndsWith("_amf", StringComparison.Ordinal) ? Acceleration.Amd
        : Acceleration.Off;

    /// <summary>Maximum CRF/CQ value of a codec.</summary>
    public static int CrfMax(VideoCodec codec) => codec is VideoCodec.Av1 or VideoCodec.Vp9 ? 63 : 51;

    /// <summary>CRF that matches a quality preset (lower = better).</summary>
    public static int CrfFor(VideoCodec codec, QualityPreset preset) => (codec, preset) switch
    {
        (VideoCodec.H264, QualityPreset.Best) => 16,
        (VideoCodec.H264, QualityPreset.Tiny) => 32,
        (VideoCodec.H265, QualityPreset.Best) => 18,
        (VideoCodec.H265, QualityPreset.Tiny) => 34,
        (_, QualityPreset.Best) => 20,
        (_, QualityPreset.Tiny) => 46,
        (VideoCodec.H264, QualityPreset.High) => 18,
        (VideoCodec.H264, QualityPreset.Balanced) => 23,
        (VideoCodec.H264, QualityPreset.Small) => 28,
        (VideoCodec.H265, QualityPreset.High) => 20,
        (VideoCodec.H265, QualityPreset.Balanced) => 26,
        (VideoCodec.H265, QualityPreset.Small) => 30,
        (_, QualityPreset.High) => 24,
        (_, QualityPreset.Balanced) => 32,
        _ => 40
    };

    // ================================================================ Resolution, aspect, frame rate

    /// <summary>Short side of a resolution preset (0 = original/custom).</summary>
    public static int ShortSide(ResolutionPreset preset) => preset switch
    {
        ResolutionPreset.P2160 => 2160,
        ResolutionPreset.P1440 => 1440,
        ResolutionPreset.P1080 => 1080,
        ResolutionPreset.P720 => 720,
        ResolutionPreset.P576 => 576,
        ResolutionPreset.P480 => 480,
        ResolutionPreset.P360 => 360,
        _ => 0
    };

    /// <summary>Width/height ratio of an aspect preset (null = original or custom).</summary>
    public static double? AspectRatio(AspectPreset preset) => preset switch
    {
        AspectPreset.R16x9 => 16d / 9d,
        AspectPreset.R4x3 => 4d / 3d,
        AspectPreset.R1x1 => 1d,
        AspectPreset.R9x16 => 9d / 16d,
        AspectPreset.R21x9 => 64d / 27d,
        AspectPreset.R4x5 => 4d / 5d,
        AspectPreset.R3x2 => 3d / 2d,
        _ => null
    };

    /// <summary>Exact FFmpeg frame rate expression ("30000/1001" for 29.97); null = keep.</summary>
    public static string? FrameRateExpression(FrameRatePreset preset, double custom) => preset switch
    {
        FrameRatePreset.Fps23976 => "24000/1001",
        FrameRatePreset.Fps24 => "24",
        FrameRatePreset.Fps25 => "25",
        FrameRatePreset.Fps2997 => "30000/1001",
        FrameRatePreset.Fps30 => "30",
        FrameRatePreset.Fps48 => "48",
        FrameRatePreset.Fps50 => "50",
        FrameRatePreset.Fps5994 => "60000/1001",
        FrameRatePreset.Fps60 => "60",
        FrameRatePreset.Fps120 => "120",
        FrameRatePreset.Custom when custom > 0 && custom <= 1000 => custom.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
        _ => null
    };

    public static double? FrameRateValue(FrameRatePreset preset, double custom) => preset switch
    {
        FrameRatePreset.Fps23976 => 24000d / 1001d,
        FrameRatePreset.Fps24 => 24,
        FrameRatePreset.Fps25 => 25,
        FrameRatePreset.Fps2997 => 30000d / 1001d,
        FrameRatePreset.Fps30 => 30,
        FrameRatePreset.Fps48 => 48,
        FrameRatePreset.Fps50 => 50,
        FrameRatePreset.Fps5994 => 60000d / 1001d,
        FrameRatePreset.Fps60 => 60,
        FrameRatePreset.Fps120 => 120,
        FrameRatePreset.Custom when custom > 0 => custom,
        _ => null
    };

    // ================================================================ Audio formats

    public static string Extension(AudioFormat format) => format switch
    {
        AudioFormat.Aac => "m4a",
        AudioFormat.Opus => "opus",
        AudioFormat.Vorbis => "ogg",
        AudioFormat.Flac => "flac",
        AudioFormat.Alac => "m4a",
        AudioFormat.Wav => "wav",
        _ => "mp3"
    };

    public static string AudioEncoder(AudioFormat format, WavDepth depth = WavDepth.Pcm16) => format switch
    {
        AudioFormat.Aac => "aac",
        AudioFormat.Opus => "libopus",
        AudioFormat.Vorbis => "libvorbis",
        AudioFormat.Flac => "flac",
        AudioFormat.Alac => "alac",
        AudioFormat.Wav => depth switch
        {
            WavDepth.Pcm24 => "pcm_s24le",
            WavDepth.Float32 => "pcm_f32le",
            _ => "pcm_s16le"
        },
        _ => "libmp3lame"
    };

    public static bool IsLossless(AudioFormat format) => format is AudioFormat.Flac or AudioFormat.Alac or AudioFormat.Wav;

    /// <summary>Formats that can carry embedded cover art via FFmpeg.</summary>
    public static bool SupportsCover(AudioFormat format) => format is AudioFormat.Mp3 or AudioFormat.Aac or AudioFormat.Flac or AudioFormat.Alac;

    /// <summary>Typical bitrate steps per format, best first.</summary>
    public static IReadOnlyList<int> BitratesFor(AudioFormat format) => format switch
    {
        AudioFormat.Mp3 => [320, 256, 224, 192, 160, 128, 112, 96, 64],
        AudioFormat.Aac => [320, 256, 192, 160, 128, 96, 64],
        AudioFormat.Opus => [256, 192, 160, 128, 96, 64, 48, 32],
        _ => []
    };

    public static IReadOnlyList<int> VideoAudioBitrates { get; } = [320, 256, 192, 160, 128, 96, 64];

    /// <summary>MP3 VBR levels offered in the UI (V0 best … V9 smallest).</summary>
    public static IReadOnlyList<int> Mp3VbrLevels { get; } = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9];

    /// <summary>Approximate average bitrate of LAME VBR levels in kbps.</summary>
    public static int Mp3VbrKbps(int level) => level switch
    {
        <= 0 => 245,
        1 => 225,
        2 => 190,
        3 => 175,
        4 => 165,
        5 => 130,
        6 => 115,
        7 => 100,
        8 => 85,
        _ => 65
    };

    public static int VorbisKbps(int quality) => quality switch
    {
        <= 0 => 64,
        1 => 80,
        2 => 96,
        3 => 112,
        4 => 128,
        5 => 160,
        6 => 192,
        7 => 224,
        8 => 256,
        9 => 320,
        _ => 500
    };

    public static IReadOnlyList<int> SampleRates { get; } = [0, 8000, 16000, 22050, 24000, 32000, 44100, 48000, 88200, 96000];

    /// <summary>Opus only accepts these rates; other values are left to FFmpeg (it picks 48 kHz).</summary>
    public static bool OpusSampleRate(int rate) => rate is 8000 or 12000 or 16000 or 24000 or 48000;

    public static AudioFormat? AudioFormatFromExtension(string? ext) => ext?.TrimStart('.').ToLowerInvariant() switch
    {
        "mp3" => AudioFormat.Mp3,
        "m4a" or "aac" or "m4b" => AudioFormat.Aac,
        "opus" => AudioFormat.Opus,
        "ogg" or "oga" => AudioFormat.Vorbis,
        "flac" => AudioFormat.Flac,
        "wav" or "wave" => AudioFormat.Wav,
        _ => null
    };

    /// <summary>File extension for an audio track copied without re-encoding.</summary>
    public static string CopyExtension(string? codec) => codec switch
    {
        "aac" => "m4a",
        "alac" => "m4a",
        "mp3" => "mp3",
        "mp2" => "mp2",
        "opus" => "opus",
        "vorbis" => "ogg",
        "flac" => "flac",
        "ac3" => "ac3",
        "eac3" => "eac3",
        "dts" => "dts",
        "wmav1" or "wmav2" or "wmapro" => "wma",
        _ when codec?.StartsWith("pcm_", StringComparison.Ordinal) == true => "wav",
        _ => "mka"
    };

    // ================================================================ Images

    public static string Extension(ImageFormat format, string sourceExtension) => format switch
    {
        ImageFormat.Png => "png",
        ImageFormat.Jpeg => "jpg",
        ImageFormat.WebP => "webp",
        ImageFormat.Avif => "avif",
        ImageFormat.Tiff => "tif",
        ImageFormat.Bmp => "bmp",
        ImageFormat.Gif => "gif",
        ImageFormat.Ico => "ico",
        ImageFormat.Tga => "tga",
        ImageFormat.Dds => "dds",
        _ => string.IsNullOrEmpty(sourceExtension) ? "png" : sourceExtension.TrimStart('.').ToLowerInvariant()
    };

    public static ImageFormat ImageFormatFromExtension(string? ext) => ext?.TrimStart('.').ToLowerInvariant() switch
    {
        "png" => ImageFormat.Png,
        "jpg" or "jpeg" or "jpe" or "jfif" => ImageFormat.Jpeg,
        "webp" => ImageFormat.WebP,
        "avif" => ImageFormat.Avif,
        "tif" or "tiff" => ImageFormat.Tiff,
        "bmp" or "dib" => ImageFormat.Bmp,
        "gif" => ImageFormat.Gif,
        "ico" => ImageFormat.Ico,
        "tga" => ImageFormat.Tga,
        "dds" => ImageFormat.Dds,
        _ => ImageFormat.Keep
    };

    public static bool SupportsAlpha(ImageFormat format) =>
        format is ImageFormat.Png or ImageFormat.WebP or ImageFormat.Avif or ImageFormat.Tiff or ImageFormat.Gif or ImageFormat.Ico
            or ImageFormat.Tga or ImageFormat.Dds;

    /// <summary>Side length as a power of two (1 … 16384).</summary>
    public static int PowerOfTwoSide(int value, PowerOfTwo mode)
    {
        if (value <= 1 || mode == PowerOfTwo.Off)
        {
            return Math.Max(1, value);
        }

        var down = 1;
        while (down * 2 <= value && down < 16384)
        {
            down *= 2;
        }

        var up = down == value ? down : Math.Min(16384, down * 2);
        return mode switch
        {
            PowerOfTwo.Down => down,
            PowerOfTwo.Up => up,
            _ => value - down <= up - value ? down : up
        };
    }

    public static bool SupportsAnimation(ImageFormat format) => format is ImageFormat.Gif or ImageFormat.WebP;

    /// <summary>Same file type, e.g. "jpeg" and "jpg".</summary>
    public static bool SameImageType(string sourceExt, ImageFormat target) =>
        target != ImageFormat.Keep && ImageFormatFromExtension(sourceExt) == target;

    public static bool SameAudioType(string sourceExt, AudioFormat target)
    {
        var ext = sourceExt.TrimStart('.').ToLowerInvariant();
        return target switch
        {
            AudioFormat.Mp3 => ext == "mp3",
            AudioFormat.Aac or AudioFormat.Alac => ext is "m4a" or "aac",
            AudioFormat.Opus => ext == "opus",
            AudioFormat.Vorbis => ext is "ogg" or "oga",
            AudioFormat.Flac => ext == "flac",
            AudioFormat.Wav => ext is "wav" or "wave",
            _ => false
        };
    }
}
