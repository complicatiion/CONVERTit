using System.Globalization;
using CONVERTit.Core.Catalog;
using CONVERTit.Core.Media;
using CONVERTit.Core.Options;

namespace CONVERTit.Core.Ffmpeg;

/// <summary>Part of the source: start and optional length in seconds.</summary>
public sealed record ClipRange(double Start, double? Duration)
{
    public static ClipRange? From(double start, double end)
    {
        start = Math.Max(0, start);
        if (start <= 0 && end <= 0)
        {
            return null;
        }

        return new ClipRange(start, end > start ? end - start : null);
    }
}

/// <summary>Arguments for one FFmpeg run (without the common prefix the runner adds).</summary>
public sealed record FfmpegCommand(IReadOnlyList<string> Args, double ExpectedSeconds, IReadOnlyList<string> Warnings)
{
    public override string ToString() => FfmpegCommands.Display(Args);
}

/// <summary>A conversion that cannot be done; Key is a UI text key.</summary>
public sealed class ConversionException(string key, params object[] args) : Exception(key)
{
    public string Key { get; } = key;

    public object[] Arguments { get; } = args;
}

/// <summary>
/// Builds FFmpeg argument lists. Pure functions: everything is derived from the probe result and the options,
/// which keeps the logic unit-testable and the command visible in the job console.
/// </summary>
public static class FfmpegCommands
{
    // ================================================================ Video

    /// <summary>Re-encodes video (convert, exact trim, boomerang).</summary>
    public static FfmpegCommand Video(MediaInfo info, string input, string output, VideoOptions o,
        ClipRange? clip = null, double fadeIn = 0, double fadeOut = 0, bool boomerang = false)
    {
        if (o.QualityMode == VideoQualityMode.TargetSize)
        {
            o = CompressPlanner.ResolveTargetSize(info, o);
        }

        var a = new List<string>();
        var warnings = new List<string>();
        var container = o.Container;
        var codec = FormatCatalog.CodecsFor(container).Contains(o.Codec) ? o.Codec : FormatCatalog.CodecsFor(container)[0];
        var encoder = FormatCatalog.VideoEncoder(codec, o.Acceleration) ?? FormatCatalog.VideoEncoder(codec, Acceleration.Off)!;

        AddInput(a, input, clip);
        var duration = Duration(info, clip);
        var video = info.MainVideo ?? throw new ConversionException("ErrNoVideoStream");

        // Picture: size/aspect, frame rate, fades.
        var filters = new List<string>();
        var (sw, sh) = video.DisplaySize;
        var geometry = VideoGeometry.Filter(sw, sh, o);
        if (geometry.Length > 0)
        {
            filters.Add(geometry);
        }

        var fps = FormatCatalog.FrameRateExpression(o.FrameRate, o.CustomFps);
        if (fps is not null)
        {
            filters.Add(o.FpsConversion switch
            {
                FpsConversion.Blend => $"framerate=fps={fps}",
                FpsConversion.Motion => $"minterpolate=fps={fps}:mi_mode=mci:mc_mode=aobmc:vsbmc=1",
                _ => $"fps={fps}"
            });
        }

        if (fadeIn > 0)
        {
            filters.Add($"fade=t=in:st=0:d={S(fadeIn)}");
        }

        if (fadeOut > 0 && duration > fadeOut)
        {
            filters.Add($"fade=t=out:st={S(duration - fadeOut)}:d={S(fadeOut)}");
        }

        var audio = ResolveVideoAudio(info, o, container, warnings);
        if (audio == VideoAudioMode.Copy && (fadeIn > 0 || fadeOut > 0))
        {
            audio = FormatCatalog.DefaultAudioFor(container);
        }

        if (boomerang)
        {
            var chain = filters.Count > 0 ? string.Join(",", filters) + "," : "";
            a.Add("-filter_complex");
            a.Add($"[0:{video.Index}]{chain}split[f][r];[r]reverse[rv];[f][rv]concat=n=2:v=1:a=0[v]");
            a.Add("-map");
            a.Add("[v]");
            audio = VideoAudioMode.Remove;
            duration *= 2;
        }
        else
        {
            a.Add("-map");
            a.Add($"0:{video.Index}");
            if (filters.Count > 0)
            {
                a.Add("-vf");
                a.Add(string.Join(",", filters));
            }
        }

        // Audio.
        if (audio != VideoAudioMode.Remove && info.Audio.Count > 0)
        {
            a.Add("-map");
            a.Add("0:a?");
            AddVideoAudioCodec(a, audio, o.AudioBitrateKbps);
            var af = AudioFades(fadeIn, fadeOut, duration);
            if (af.Length > 0 && audio != VideoAudioMode.Copy)
            {
                a.Add("-af");
                a.Add(af);
            }
        }
        else
        {
            a.Add("-an");
        }

        // Subtitles.
        if (!boomerang && o.KeepSubtitles)
        {
            AddSubtitles(a, info, container, warnings);
        }

        // Video encoder.
        a.Add("-c:v");
        a.Add(encoder);
        AddVideoQuality(a, codec, encoder, o);
        AddVideoSpeed(a, encoder, o.Speed);
        if (o.Compatible8Bit)
        {
            a.Add("-pix_fmt");
            a.Add(encoder.EndsWith("_qsv", StringComparison.Ordinal) || encoder.EndsWith("_amf", StringComparison.Ordinal) ? "nv12" : "yuv420p");
        }

        if (encoder == "libx265")
        {
            a.Add("-x265-params");
            a.Add("log-level=error");
        }
        else if (encoder == "libvpx-vp9")
        {
            a.Add("-row-mt");
            a.Add("1");
        }

        if (codec == VideoCodec.H265 && container is VideoContainer.Mp4 or VideoContainer.Mov)
        {
            a.Add("-tag:v");
            a.Add("hvc1");
        }

        AddMetadata(a, o.KeepMetadata);
        if (o.FastStart && container is VideoContainer.Mp4 or VideoContainer.Mov)
        {
            AddFastStart(a);
        }

        a.Add(output);
        return new FfmpegCommand(a, duration, warnings);
    }

    /// <summary>Re-encode options that stay as close as possible to the source (exact trim in the same format).</summary>
    public static VideoOptions SameFormatOptions(MediaInfo info)
    {
        var container = FormatCatalog.ContainerFromExtension(MediaTypes.Extension(info.Path)) ?? VideoContainer.Mp4;
        var sourceCodec = FormatCatalog.CodecFromFfmpeg(info.MainVideo?.Codec);
        var codecs = FormatCatalog.CodecsFor(container);
        var tenBit = info.MainVideo?.PixelFormat.Contains("10", StringComparison.Ordinal) == true;
        return new VideoOptions
        {
            Container = container,
            Codec = sourceCodec is { } c && codecs.Contains(c) ? c : codecs[0],
            QualityMode = VideoQualityMode.Preset,
            QualityPreset = QualityPreset.High,
            Speed = EncoderSpeed.Balanced,
            Compatible8Bit = !tenBit,
            Audio = VideoAudioMode.Auto,
            AudioBitrateKbps = 256,
            KeepSubtitles = true,
            KeepMetadata = true,
            FastStart = true
        };
    }

    /// <summary>Cuts without re-encoding (fast, starts at the keyframe before the start time).</summary>
    public static FfmpegCommand VideoCopy(MediaInfo info, string input, string output, ClipRange? clip)
    {
        var a = new List<string>();
        AddInput(a, input, clip);
        a.AddRange(["-map", "0:v?", "-map", "0:a?", "-map", "0:s?", "-c", "copy", "-avoid_negative_ts", "make_zero"]);
        AddMetadata(a, true);
        if (IsMp4Like(output))
        {
            AddFastStart(a);
        }

        a.Add(output);
        return new FfmpegCommand(a, Duration(info, clip), []);
    }

    /// <summary>Repeats a file without re-encoding (count = total plays) or up to a target length.</summary>
    public static FfmpegCommand Loop(string input, string output, double segmentSeconds, int playCount, double? targetSeconds)
    {
        playCount = Math.Max(1, playCount);
        if (targetSeconds is > 0 && segmentSeconds > 0)
        {
            playCount = (int)Math.Ceiling(targetSeconds.Value / segmentSeconds);
        }

        var a = new List<string> { "-stream_loop", (playCount - 1).ToString(CultureInfo.InvariantCulture), "-i", input };
        a.AddRange(["-map", "0", "-c", "copy"]);
        if (targetSeconds is > 0)
        {
            a.Add("-t");
            a.Add(S(targetSeconds.Value));
        }

        if (IsMp4Like(output))
        {
            AddFastStart(a);
        }

        a.Add(output);
        var expected = targetSeconds is > 0 ? targetSeconds.Value : segmentSeconds * playCount;
        return new FfmpegCommand(a, expected, []);
    }

    /// <summary>Changes the container, streams are copied.</summary>
    public static FfmpegCommand Remux(MediaInfo info, string input, string output, RemuxOptions o)
    {
        var problem = RemuxProblem(info, o.Container);
        if (problem is not null)
        {
            throw new ConversionException(problem.Value.Key, problem.Value.Codec);
        }

        var a = new List<string> { "-i", input };
        var warnings = new List<string>();
        foreach (var v in info.Video.Where(v => !v.IsCoverArt))
        {
            a.Add("-map");
            a.Add($"0:{v.Index}");
        }

        if (info.Audio.Count > 0)
        {
            a.Add("-map");
            a.Add("0:a?");
        }

        a.AddRange(["-c:v", "copy", "-c:a", "copy"]);
        if (o.KeepSubtitles)
        {
            AddSubtitles(a, info, o.Container, warnings);
        }

        if (FormatCatalog.CodecFromFfmpeg(info.MainVideo?.Codec) == VideoCodec.H265 && o.Container is VideoContainer.Mp4 or VideoContainer.Mov)
        {
            a.Add("-tag:v");
            a.Add("hvc1");
        }

        AddMetadata(a, true);
        if (o.FastStart && o.Container is VideoContainer.Mp4 or VideoContainer.Mov)
        {
            AddFastStart(a);
        }

        a.Add(output);
        return new FfmpegCommand(a, info.DurationSeconds, warnings);
    }

    /// <summary>Why streams cannot be copied into the container (null = fine).</summary>
    public static (string Key, string Codec)? RemuxProblem(MediaInfo info, VideoContainer container)
    {
        var video = info.MainVideo?.Codec ?? "";
        if (video.Length > 0)
        {
            var ok = container switch
            {
                VideoContainer.WebM => video is "vp8" or "vp9" or "av1",
                VideoContainer.Mp4 => video is "h264" or "hevc" or "av1" or "vp9" or "mpeg4" or "mpeg2video" or "mjpeg",
                VideoContainer.Mov => video is "h264" or "hevc" or "prores" or "mpeg4" or "mjpeg" or "dnxhd",
                VideoContainer.Avi => video is "h264" or "mpeg4" or "mjpeg" or "msmpeg4v3" or "mpeg2video",
                _ => true
            };
            if (!ok)
            {
                return ("ErrRemuxVideo", video);
            }
        }

        foreach (var audio in info.Audio)
        {
            if (!FormatCatalog.AudioCopyAllowed(container, audio.Codec))
            {
                return ("ErrRemuxAudio", audio.Codec);
            }
        }

        return null;
    }

    // ================================================================ Audio

    /// <summary>Encodes one audio track (convert, exact trim, extract with conversion).</summary>
    public static FfmpegCommand Audio(MediaInfo info, string input, string output, AudioOptions o,
        ClipRange? clip = null, double fadeIn = 0, double fadeOut = 0, int? streamIndex = null, bool boomerang = false)
    {
        var a = new List<string>();
        var warnings = new List<string>();
        AddInput(a, input, clip);
        var duration = Duration(info, clip);
        var track = streamIndex is { } si ? info.Audio.FirstOrDefault(x => x.Index == si) : info.MainAudio;
        if (track is null)
        {
            throw new ConversionException("ErrNoAudioStream");
        }

        var fades = AudioChain(AudioFades(fadeIn, fadeOut, duration), o);
        if (boomerang)
        {
            var chain = fades.Length > 0 ? fades + "," : "";
            a.Add("-filter_complex");
            a.Add($"[0:{track.Index}]{chain}asplit[a1][a2];[a2]areverse[ar];[a1][ar]concat=n=2:v=0:a=1[a]");
            a.Add("-map");
            a.Add("[a]");
            duration *= 2;
        }
        else
        {
            a.Add("-map");
            a.Add($"0:{track.Index}");
            if (fades.Length > 0)
            {
                a.Add("-af");
                a.Add(fades);
            }
        }

        // Cover art (an attached picture stream) is copied when the target can hold it.
        var cover = info.CoverArt;
        if (!boomerang && o.KeepCover && cover is not null && FormatCatalog.SupportsCover(o.Format))
        {
            a.AddRange(["-map", $"0:{cover.Index}", "-c:v", "copy", "-disposition:v:0", "attached_pic"]);
        }
        else
        {
            a.Add("-vn");
        }

        a.Add("-sn");
        a.Add("-c:a");
        a.Add(FormatCatalog.AudioEncoder(o.Format, o.WavDepth));
        switch (o.Format)
        {
            case AudioFormat.Mp3:
                if (o.Mp3Mode == BitrateMode.Vbr)
                {
                    a.Add("-q:a");
                    a.Add(Math.Clamp(o.Mp3VbrLevel, 0, 9).ToString(CultureInfo.InvariantCulture));
                }
                else
                {
                    a.Add("-b:a");
                    a.Add(EffectiveBitrate(info, track, o, warnings) + "k");
                }

                a.AddRange(["-id3v2_version", "3"]);
                break;
            case AudioFormat.Aac:
                a.Add("-b:a");
                a.Add(EffectiveBitrate(info, track, o, warnings) + "k");
                AddFastStart(a);
                break;
            case AudioFormat.Opus:
                a.Add("-b:a");
                a.Add(EffectiveBitrate(info, track, o, warnings) + "k");
                a.AddRange(["-vbr", "on"]);
                break;
            case AudioFormat.Vorbis:
                a.Add("-q:a");
                a.Add(Math.Clamp(o.VorbisQuality, 0, 10).ToString(CultureInfo.InvariantCulture));
                break;
            case AudioFormat.Flac:
                a.Add("-compression_level");
                a.Add(Math.Clamp(o.FlacLevel, 0, 8).ToString(CultureInfo.InvariantCulture));
                break;
            case AudioFormat.Alac:
                AddFastStart(a);
                break;
        }

        if (o.SampleRate > 0 && (o.Format != AudioFormat.Opus || FormatCatalog.OpusSampleRate(o.SampleRate)))
        {
            a.Add("-ar");
            a.Add(o.SampleRate.ToString(CultureInfo.InvariantCulture));
        }
        else if (o.Loudness != LoudnessTarget.Off && o.Format != AudioFormat.Opus)
        {
            // loudnorm resamples to 192 kHz internally: return to the source rate.
            a.Add("-ar");
            a.Add((track.SampleRate > 0 ? track.SampleRate : 48000).ToString(CultureInfo.InvariantCulture));
        }

        if (o.Channels != ChannelMode.Original)
        {
            a.Add("-ac");
            a.Add(o.Channels == ChannelMode.Mono ? "1" : "2");
        }

        if (!track.IsLossless && FormatCatalog.IsLossless(o.Format))
        {
            warnings.Add("WarnLossyToLossless");
        }

        AddMetadata(a, o.KeepMetadata);
        a.Add(output);
        return new FfmpegCommand(a, duration, warnings);
    }

    /// <summary>Copies one audio track as it is (extract, fast trim).</summary>
    public static FfmpegCommand AudioCopy(MediaInfo info, string input, string output, int? streamIndex, ClipRange? clip)
    {
        var a = new List<string>();
        AddInput(a, input, clip);
        var track = streamIndex is { } si ? info.Audio.FirstOrDefault(x => x.Index == si) : info.MainAudio;
        if (track is null)
        {
            throw new ConversionException("ErrNoAudioStream");
        }

        a.AddRange(["-map", $"0:{track.Index}", "-vn", "-sn", "-c:a", "copy"]);
        AddMetadata(a, true);
        if (IsMp4Like(output))
        {
            AddFastStart(a);
        }

        a.Add(output);
        return new FfmpegCommand(a, Duration(info, clip), []);
    }

    /// <summary>
    /// Bitrate actually used: with "not above source", a lossy source caps the target at the next standard step.
    /// </summary>
    public static int EffectiveBitrate(MediaInfo info, AudioStreamInfo track, AudioOptions o, List<string>? warnings = null)
    {
        var target = o.BitrateKbps > 0 ? o.BitrateKbps : 192;
        if (!o.NotAboveSource || track.IsLossless)
        {
            return target;
        }

        var sourceBits = track.Bitrate > 0 ? track.Bitrate : info.Audio.Count == 1 && info.Video.All(v => v.IsCoverArt) ? info.Bitrate : 0;
        if (sourceBits <= 0)
        {
            return target;
        }

        var sourceKbps = (int)Math.Round(sourceBits / 1000d);
        if (sourceKbps >= target)
        {
            return target;
        }

        var step = FormatCatalog.BitratesFor(o.Format).Where(l => l >= sourceKbps).DefaultIfEmpty(target).Min();
        if (step < target)
        {
            warnings?.Add("WarnBitrateCapped");
            return step;
        }

        return target;
    }

    // ================================================================ Animation

    /// <summary>Video to animated GIF (two-pass palette) or animated WebP.</summary>
    public static FfmpegCommand Animation(MediaInfo info, string input, string output, GifOptions o)
    {
        var a = new List<string>();
        var clip = new ClipRange(Math.Max(0, o.Start), o.Duration > 0 ? o.Duration : null);
        AddInput(a, input, clip);
        var video = info.MainVideo ?? throw new ConversionException("ErrNoVideoStream");
        var duration = Duration(info, clip);

        var chain = $"fps={Math.Clamp(o.Fps, 1, 60)}";
        if (o.Width > 0)
        {
            chain += $",scale={o.Width}:-2:flags=lanczos";
        }

        if (o.Boomerang)
        {
            chain += ",split[f][r];[r]reverse[rv];[f][rv]concat=n=2:v=1:a=0";
            duration *= 2;
        }

        a.Add("-filter_complex");
        if (o.Format == AnimationFormat.Gif)
        {
            var dither = o.Dither switch
            {
                GifDither.FloydSteinberg => "dither=floyd_steinberg",
                GifDither.None => "dither=none",
                _ => "dither=bayer:bayer_scale=5"
            };
            a.Add($"[0:{video.Index}]{chain},split[s0][s1];[s0]palettegen=stats_mode=diff[p];[s1][p]paletteuse={dither}:diff_mode=rectangle[out]");
            a.AddRange(["-map", "[out]", "-an"]);
            // GIF muxer: -1 = play once, 0 = endless, N = repeat N times.
            a.Add("-loop");
            a.Add(o.LoopCount <= 0 ? "0" : (o.LoopCount == 1 ? "-1" : (o.LoopCount - 1).ToString(CultureInfo.InvariantCulture)));
        }
        else
        {
            a.Add($"[0:{video.Index}]{chain}[out]");
            a.AddRange(["-map", "[out]", "-an", "-c:v", "libwebp", "-lossless", "0", "-q:v",
                Math.Clamp(o.Quality, 0, 100).ToString(CultureInfo.InvariantCulture), "-compression_level", "4",
                "-loop", Math.Max(0, o.LoopCount).ToString(CultureInfo.InvariantCulture)]);
        }

        a.Add(output);
        return new FfmpegCommand(a, duration, []);
    }

    // ================================================================ Previews

    /// <summary>One frame as PNG on stdout (trim timeline, info panel).</summary>
    public static IReadOnlyList<string> FrameGrab(string input, double seconds, int height) =>
    [
        "-ss", S(Math.Max(0, seconds)), "-i", input, "-frames:v", "1", "-an", "-sn",
        "-vf", $"scale=-2:{Math.Max(16, height)}", "-f", "image2pipe", "-c:v", "png", "pipe:1"
    ];

    // ================================================================ Helpers

    /// <summary>Seconds in FFmpeg notation (invariant, up to milliseconds).</summary>
    public static string S(double seconds) => Math.Max(0, seconds).ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>Command line for the console (quoted where needed).</summary>
    public static string Display(IEnumerable<string> args) =>
        "ffmpeg " + string.Join(" ", args.Select(arg => arg.Length == 0 || arg.Any(c => char.IsWhiteSpace(c) || c is '"' or ';' or '[' or '&')
            ? "\"" + arg.Replace("\"", "\\\"") + "\""
            : arg));

    private static void AddInput(List<string> a, string input, ClipRange? clip)
    {
        // Input options: accurate seek (decoding starts at the keyframe, output starts exactly at Start)
        // and a read limit, so filters such as reverse only see the selected part.
        if (clip is not null && clip.Start > 0)
        {
            a.Add("-ss");
            a.Add(S(clip.Start));
        }

        if (clip?.Duration is > 0)
        {
            a.Add("-t");
            a.Add(S(clip.Duration.Value));
        }

        a.Add("-i");
        a.Add(input);
    }

    private static double Duration(MediaInfo info, ClipRange? clip)
    {
        var total = info.DurationSeconds;
        if (clip is null)
        {
            return total;
        }

        var rest = Math.Max(0, total - clip.Start);
        return clip.Duration is > 0 ? (total > 0 ? Math.Min(clip.Duration.Value, rest) : clip.Duration.Value) : rest;
    }

    /// <summary>Silence trimming (both ends) and loudness normalization after the fades.</summary>
    public static string AudioChain(string fades, AudioOptions o)
    {
        var parts = new List<string>();
        if (fades.Length > 0)
        {
            parts.Add(fades);
        }

        if (o.TrimSilence)
        {
            // Start: drop leading silence; end: the same on the reversed signal.
            const string trim = "silenceremove=start_periods=1:start_duration=0.02:start_threshold=-50dB";
            parts.Add($"{trim},areverse,{trim},areverse");
        }

        var lufs = LoudnessLufs(o.Loudness);
        if (lufs is not null)
        {
            parts.Add($"loudnorm=I={lufs}:TP=-1.5:LRA=11");
        }

        return string.Join(",", parts);
    }

    /// <summary>Integrated loudness target in LUFS (null = off).</summary>
    public static string? LoudnessLufs(LoudnessTarget target) => target switch
    {
        LoudnessTarget.Lufs14 => "-14",
        LoudnessTarget.Lufs16 => "-16",
        LoudnessTarget.Lufs23 => "-23",
        _ => null
    };

    /// <summary>
    /// Two-pass bitrate encode (libx264): pass 1 only writes statistics (no audio, null muxer), pass 2 the file.
    /// Hits a target size much more exactly than a single pass.
    /// </summary>
    public static (FfmpegCommand Pass1, FfmpegCommand Pass2) TwoPass(FfmpegCommand command, string passLogPrefix)
    {
        var args = command.Args.ToList();
        var output = args[^1];
        args.RemoveAt(args.Count - 1);

        var pass1 = new List<string>(args) { "-pass", "1", "-passlogfile", passLogPrefix, "-an", "-sn", "-f", "null", "-" };
        var pass2 = new List<string>(args) { "-pass", "2", "-passlogfile", passLogPrefix, output };
        return (new FfmpegCommand(pass1, command.ExpectedSeconds, []), new FfmpegCommand(pass2, command.ExpectedSeconds, command.Warnings));
    }

    private static string AudioFades(double fadeIn, double fadeOut, double duration)
    {
        var parts = new List<string>();
        if (fadeIn > 0)
        {
            parts.Add($"afade=t=in:st=0:d={S(fadeIn)}");
        }

        if (fadeOut > 0 && duration > fadeOut)
        {
            parts.Add($"afade=t=out:st={S(duration - fadeOut)}:d={S(fadeOut)}");
        }

        return string.Join(",", parts);
    }

    private static VideoAudioMode ResolveVideoAudio(MediaInfo info, VideoOptions o, VideoContainer container, List<string> warnings)
    {
        if (info.Audio.Count == 0 || o.Audio == VideoAudioMode.Remove)
        {
            return VideoAudioMode.Remove;
        }

        var copyAllowed = info.Audio.All(t => FormatCatalog.AudioCopyAllowed(container, t.Codec));
        switch (o.Audio)
        {
            case VideoAudioMode.Auto:
                return copyAllowed ? VideoAudioMode.Copy : FormatCatalog.DefaultAudioFor(container);
            case VideoAudioMode.Copy:
                if (copyAllowed)
                {
                    return VideoAudioMode.Copy;
                }

                warnings.Add("WarnAudioReencoded");
                return FormatCatalog.DefaultAudioFor(container);
            default:
                return FormatCatalog.AudioModesFor(container).Contains(o.Audio) ? o.Audio : FormatCatalog.DefaultAudioFor(container);
        }
    }

    private static void AddVideoAudioCodec(List<string> a, VideoAudioMode mode, int kbps)
    {
        var bitrate = Math.Clamp(kbps, 32, 512) + "k";
        switch (mode)
        {
            case VideoAudioMode.Copy:
                a.AddRange(["-c:a", "copy"]);
                break;
            case VideoAudioMode.Opus:
                a.AddRange(["-c:a", "libopus", "-b:a", bitrate]);
                break;
            case VideoAudioMode.Mp3:
                a.AddRange(["-c:a", "libmp3lame", "-b:a", bitrate]);
                break;
            default:
                a.AddRange(["-c:a", "aac", "-b:a", bitrate]);
                break;
        }
    }

    private static void AddSubtitles(List<string> a, MediaInfo info, VideoContainer container, List<string> warnings)
    {
        if (info.Subtitles.Count == 0)
        {
            return;
        }

        switch (container)
        {
            case VideoContainer.Mkv:
                a.AddRange(["-map", "0:s?", "-c:s", "copy"]);
                return;
            case VideoContainer.Avi:
                warnings.Add("WarnSubtitlesDropped");
                return;
        }

        var text = info.Subtitles.Where(s => !s.IsBitmap).ToList();
        if (text.Count < info.Subtitles.Count)
        {
            warnings.Add("WarnBitmapSubtitlesDropped");
        }

        foreach (var s in text)
        {
            a.Add("-map");
            a.Add($"0:{s.Index}");
        }

        if (text.Count > 0)
        {
            a.Add("-c:s");
            a.Add(container == VideoContainer.WebM ? "webvtt" : "mov_text");
        }
    }

    private static void AddVideoQuality(List<string> a, VideoCodec codec, string encoder, VideoOptions o)
    {
        var nvenc = encoder.EndsWith("_nvenc", StringComparison.Ordinal);
        var qsv = encoder.EndsWith("_qsv", StringComparison.Ordinal);
        var amf = encoder.EndsWith("_amf", StringComparison.Ordinal);

        if (o.QualityMode == VideoQualityMode.Bitrate)
        {
            var kbps = Math.Max(100, o.BitrateKbps);
            var b = kbps + "k";
            var max = (int)(kbps * 1.5) + "k";
            var buf = kbps * 2 + "k";
            if (nvenc)
            {
                a.AddRange(["-rc", "vbr", "-b:v", b, "-maxrate", max, "-bufsize", buf]);
            }
            else if (amf)
            {
                a.AddRange(["-rc", "vbr_peak", "-b:v", b, "-maxrate", max]);
            }
            else if (encoder is "libx264" or "libx265" || qsv)
            {
                a.AddRange(["-b:v", b, "-maxrate", max, "-bufsize", buf]);
            }
            else
            {
                a.AddRange(["-b:v", b]);
            }

            return;
        }

        var crf = o.QualityMode == VideoQualityMode.Preset
            ? FormatCatalog.CrfFor(codec, o.QualityPreset)
            : Math.Clamp(o.Crf, 0, FormatCatalog.CrfMax(codec));
        var q = crf.ToString(CultureInfo.InvariantCulture);
        if (nvenc)
        {
            a.AddRange(["-rc", "vbr", "-cq", q, "-b:v", "0"]);
        }
        else if (qsv)
        {
            a.AddRange(["-global_quality", q]);
        }
        else if (amf)
        {
            a.AddRange(["-rc", "cqp", "-qp_i", q, "-qp_p", q]);
            if (codec != VideoCodec.Av1)
            {
                a.AddRange(["-qp_b", q]);
            }
        }
        else if (encoder == "libvpx-vp9")
        {
            a.AddRange(["-crf", q, "-b:v", "0"]);
        }
        else
        {
            a.AddRange(["-crf", q]);
        }
    }

    private static void AddVideoSpeed(List<string> a, string encoder, EncoderSpeed speed)
    {
        var i = (int)speed;
        if (encoder is "libx264" or "libx265" || encoder.EndsWith("_qsv", StringComparison.Ordinal))
        {
            a.AddRange(["-preset", new[] { "veryfast", "faster", "medium", "slow", "veryslow" }[i]]);
        }
        else if (encoder == "libsvtav1")
        {
            a.AddRange(["-preset", new[] { "12", "10", "8", "5", "3" }[i]]);
        }
        else if (encoder == "libvpx-vp9")
        {
            a.AddRange(["-deadline", "good", "-cpu-used", new[] { "5", "4", "2", "1", "0" }[i]]);
        }
        else if (encoder.EndsWith("_nvenc", StringComparison.Ordinal))
        {
            a.AddRange(["-preset", new[] { "p1", "p3", "p4", "p6", "p7" }[i]]);
        }
        else if (encoder.EndsWith("_amf", StringComparison.Ordinal))
        {
            a.AddRange(["-quality", new[] { "speed", "speed", "balanced", "quality", "quality" }[i]]);
        }
    }

    private static void AddMetadata(List<string> a, bool keep)
    {
        a.Add("-map_metadata");
        a.Add(keep ? "0" : "-1");
        if (!keep)
        {
            a.AddRange(["-map_chapters", "-1"]);
        }
    }

    private static void AddFastStart(List<string> a) => a.AddRange(["-movflags", "+faststart"]);

    private static bool IsMp4Like(string path) =>
        MediaTypes.Extension(path) is "mp4" or "m4v" or "mov" or "m4a" or "m4b";
}
