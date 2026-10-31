using System.Globalization;
using System.Text.Json;
using CONVERTit.Core.Ffmpeg;
using CONVERTit.Core.Media;

namespace CONVERTit.Engines.Ffmpeg;

/// <summary>Reads streams and format of a media file with ffprobe (JSON output).</summary>
public sealed class FfprobeService(FfmpegRunner runner)
{
    public async Task<MediaInfo> ProbeAsync(string path, CancellationToken cancellationToken = default)
    {
        var ffprobe = runner.Locator.FfprobePath ?? throw new ConversionException("ErrFfmpegMissing");
        var (code, output, error) = await runner.CaptureAsync(ffprobe,
            ["-v", "error", "-print_format", "json", "-show_format", "-show_streams", path],
            TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
        if (code != 0 || string.IsNullOrWhiteSpace(output))
        {
            throw new ConversionException("ErrProbeFailed", string.IsNullOrWhiteSpace(error) ? $"exit {code}" : error.Trim());
        }

        return Parse(path, output);
    }

    /// <summary>Parses ffprobe JSON (public for tests).</summary>
    public static MediaInfo Parse(string path, string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var video = new List<VideoStreamInfo>();
        var audio = new List<AudioStreamInfo>();
        var subtitles = new List<SubtitleStreamInfo>();

        if (root.TryGetProperty("streams", out var streams) && streams.ValueKind == JsonValueKind.Array)
        {
            foreach (var s in streams.EnumerateArray())
            {
                var index = Int(s, "index");
                var type = Str(s, "codec_type");
                var codec = Str(s, "codec_name");
                var language = s.TryGetProperty("tags", out var tags) ? Str(tags, "language") : "";
                switch (type)
                {
                    case "video":
                        var attached = s.TryGetProperty("disposition", out var disposition) && Int(disposition, "attached_pic") == 1;
                        video.Add(new VideoStreamInfo
                        {
                            Index = index,
                            Codec = codec,
                            Width = Int(s, "width"),
                            Height = Int(s, "height"),
                            PixelFormat = Str(s, "pix_fmt"),
                            Fps = Rate(Str(s, "avg_frame_rate")) is var avg and > 0 ? avg : Rate(Str(s, "r_frame_rate")),
                            Bitrate = Long(s, "bit_rate"),
                            Rotation = Rotation(s),
                            IsCoverArt = attached || codec is "mjpeg" or "png" && Str(s, "avg_frame_rate") == "0/0"
                        });
                        break;
                    case "audio":
                        audio.Add(new AudioStreamInfo
                        {
                            Index = index,
                            Codec = codec,
                            SampleRate = Int(s, "sample_rate"),
                            Channels = Int(s, "channels"),
                            ChannelLayout = Str(s, "channel_layout"),
                            Bitrate = Long(s, "bit_rate"),
                            Language = language
                        });
                        break;
                    case "subtitle":
                        subtitles.Add(new SubtitleStreamInfo { Index = index, Codec = codec, Language = language });
                        break;
                }
            }
        }

        var format = root.TryGetProperty("format", out var f) ? f : default;
        var hasFormat = format.ValueKind == JsonValueKind.Object;
        var duration = hasFormat ? Double(format, "duration") : 0;
        var kind = video.Any(v => !v.IsCoverArt) ? MediaKind.Video : audio.Count > 0 ? MediaKind.Audio : MediaTypes.Detect(path);

        long size = hasFormat ? Long(format, "size") : 0;
        if (size <= 0)
        {
            try
            {
                size = new FileInfo(path).Length;
            }
            catch
            {
                size = 0;
            }
        }

        return new MediaInfo
        {
            Path = path,
            Kind = kind,
            FileSize = size,
            Format = hasFormat ? Str(format, "format_long_name") is { Length: > 0 } longName ? longName : Str(format, "format_name") : "",
            DurationSeconds = duration,
            Bitrate = hasFormat ? Long(format, "bit_rate") : 0,
            Video = video,
            Audio = audio,
            Subtitles = subtitles
        };
    }

    private static int Rotation(JsonElement stream)
    {
        if (stream.TryGetProperty("side_data_list", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in list.EnumerateArray())
            {
                if (item.TryGetProperty("rotation", out var r) && r.ValueKind == JsonValueKind.Number)
                {
                    return (int)Math.Round(r.GetDouble());
                }
            }
        }

        if (stream.TryGetProperty("tags", out var tags) && int.TryParse(Str(tags, "rotate"), out var rotate))
        {
            return rotate;
        }

        return 0;
    }

    private static double Rate(string value)
    {
        var parts = value.Split('/');
        if (parts.Length == 2
            && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var n)
            && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
            && d > 0)
        {
            return n / d;
        }

        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
    }

    private static string Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var p)
            ? p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : p.ValueKind == JsonValueKind.Number ? p.GetRawText() : ""
            : "";

    private static int Int(JsonElement e, string name) =>
        int.TryParse(Str(e, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;

    private static long Long(JsonElement e, string name) =>
        long.TryParse(Str(e, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;

    private static double Double(JsonElement e, string name) =>
        double.TryParse(Str(e, name), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
}
