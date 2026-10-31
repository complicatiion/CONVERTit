using CONVERTit.Core.Catalog;
using CONVERTit.Core.Ffmpeg;
using CONVERTit.Core.Media;
using CONVERTit.Core.Options;

namespace CONVERTit.Core.Estimation;

/// <summary>
/// Rough output size before converting. Exact for constant bitrates and WAV; for quality modes (CRF, VBR, FLAC)
/// it is a rule-of-thumb estimate and shown with "≈".
/// </summary>
public static class SizeEstimator
{
    public static long? Audio(MediaInfo info, AudioOptions o, double? durationOverride = null)
    {
        var duration = durationOverride ?? info.DurationSeconds;
        var track = info.MainAudio;
        if (duration <= 0 || track is null)
        {
            return null;
        }

        var rate = o.SampleRate > 0 ? o.SampleRate : (track.SampleRate > 0 ? track.SampleRate : 44100);
        var channels = o.Channels switch
        {
            ChannelMode.Mono => 1,
            ChannelMode.Stereo => 2,
            _ => track.Channels > 0 ? track.Channels : 2
        };
        var pcmBitsPerSecond = (double)rate * channels * 16;

        double bitsPerSecond = o.Format switch
        {
            AudioFormat.Mp3 when o.Mp3Mode == BitrateMode.Vbr => FormatCatalog.Mp3VbrKbps(o.Mp3VbrLevel) * 1000d,
            AudioFormat.Mp3 or AudioFormat.Aac or AudioFormat.Opus => FfmpegCommands.EffectiveBitrate(info, track, o) * 1000d,
            AudioFormat.Vorbis => FormatCatalog.VorbisKbps(o.VorbisQuality) * 1000d,
            AudioFormat.Flac => pcmBitsPerSecond * 0.58,
            AudioFormat.Alac => pcmBitsPerSecond * 0.62,
            AudioFormat.Wav => rate * channels * (o.WavDepth switch { WavDepth.Pcm24 => 24d, WavDepth.Float32 => 32d, _ => 16d }),
            _ => 192_000
        };

        return (long)(bitsPerSecond * duration / 8);
    }

    public static long? Video(MediaInfo info, VideoOptions o, double? durationOverride = null)
    {
        var duration = durationOverride ?? info.DurationSeconds;
        var video = info.MainVideo;
        if (duration <= 0 || video is null)
        {
            return null;
        }

        var audioBits = 0d;
        if (o.Audio != VideoAudioMode.Remove && info.Audio.Count > 0)
        {
            var copy = o.Audio == VideoAudioMode.Copy || (o.Audio == VideoAudioMode.Auto
                && info.Audio.All(t => FormatCatalog.AudioCopyAllowed(o.Container, t.Codec)));
            audioBits = copy
                ? info.Audio.Sum(t => t.Bitrate > 0 ? t.Bitrate : 160_000)
                : info.Audio.Count * Math.Clamp(o.AudioBitrateKbps, 32, 512) * 1000d;
        }

        if (o.QualityMode == VideoQualityMode.TargetSize && info.FileSize > 0)
        {
            var fraction = info.DurationSeconds > 0 ? Math.Min(1, duration / info.DurationSeconds) : 1;
            return (long)(info.FileSize * Math.Clamp(o.TargetPercent, 5, 100) / 100d * fraction);
        }

        if (o.QualityMode == VideoQualityMode.Bitrate)
        {
            return (long)((o.BitrateKbps * 1000d + audioBits) * duration / 8);
        }

        var (sw, sh) = video.DisplaySize;
        var size = VideoGeometry.Compute(sw, sh, o);
        var w = size?.Width ?? sw;
        var h = size?.Height ?? sh;
        var fps = FormatCatalog.FrameRateValue(o.FrameRate, o.CustomFps) ?? (video.Fps > 0 ? video.Fps : 30);
        if (w <= 0 || h <= 0)
        {
            return null;
        }

        var codec = FormatCatalog.CodecsFor(o.Container).Contains(o.Codec) ? o.Codec : FormatCatalog.CodecsFor(o.Container)[0];
        var crf = o.QualityMode == VideoQualityMode.Preset
            ? FormatCatalog.CrfFor(codec, o.QualityPreset)
            : Math.Clamp(o.Crf, 0, FormatCatalog.CrfMax(codec));
        var balanced = FormatCatalog.CrfFor(codec, QualityPreset.Balanced);

        // Bits per pixel at the "balanced" quality; every 6 CRF steps roughly double or halve the size.
        var bpp = codec switch
        {
            VideoCodec.H265 => 0.045,
            VideoCodec.Av1 => 0.038,
            VideoCodec.Vp9 => 0.045,
            _ => 0.075
        } * Math.Pow(2, (balanced - crf) / 6d);

        // Large frames need fewer bits per pixel.
        var pixels = (double)w * h;
        if (pixels > 2_100_000)
        {
            bpp *= 0.75;
        }
        else if (pixels < 500_000)
        {
            bpp *= 1.3;
        }

        var videoBits = bpp * pixels * fps;
        return (long)((videoBits + audioBits) * duration / 8);
    }
}
