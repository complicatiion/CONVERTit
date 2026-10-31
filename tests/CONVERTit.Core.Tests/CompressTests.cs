using System.Text.RegularExpressions;
using CONVERTit.Core.Catalog;
using CONVERTit.Core.Ffmpeg;
using CONVERTit.Core.Media;
using CONVERTit.Core.Options;
using CONVERTit.Engines.Ffmpeg;
using Xunit;

namespace CONVERTit.Core.Tests;

public class CompressTests
{
    // 10 minutes 1080p H.264 at ~8 Mbit/s with AAC 192 kbps, 600 MB.
    private const string ClipJson = """
        {"streams":[
          {"index":0,"codec_name":"h264","codec_type":"video","width":1920,"height":1080,"pix_fmt":"yuv420p","avg_frame_rate":"60/1","bit_rate":"8000000"},
          {"index":1,"codec_name":"aac","codec_type":"audio","sample_rate":"48000","channels":2,"bit_rate":"192000"}],
         "format":{"format_name":"mov,mp4","duration":"600","bit_rate":"8200000","size":"629145600"}}
        """;

    private static MediaInfo Clip => FfprobeService.Parse(@"C:\v\clip.mp4", ClipJson);

    [Fact]
    public void Target_size_gives_video_bitrate_below_the_limit()
    {
        var o = new CompressOptions { Mode = CompressMode.TargetSize, TargetMb = 25, VideoAudioKbps = 128 };
        var warnings = new List<string>();
        var v = CompressPlanner.Video(Clip, o, warnings);
        Assert.Equal(VideoQualityMode.Bitrate, v.QualityMode);
        var totalBytes = (v.BitrateKbps + 128) * 1000d * 600 / 8;
        Assert.True(totalBytes < 25 * 1024 * 1024, $"{totalBytes} bytes");
        Assert.True(totalBytes > 25 * 1024 * 1024 * 0.85, $"{totalBytes} bytes");
    }

    [Fact]
    public void Target_above_source_is_capped()
    {
        var o = new CompressOptions { Mode = CompressMode.TargetSize, TargetMb = 2000 };
        var warnings = new List<string>();
        var v = CompressPlanner.Video(Clip, o, warnings);
        Assert.True(v.BitrateKbps <= 7200);
        Assert.Contains("WarnTargetAboveSource", warnings);
    }

    [Fact]
    public void Percent_mode_uses_the_source_size()
    {
        Assert.Equal((long?)314_572_800, CompressPlanner.TargetBytes(629_145_600, new CompressOptions { Mode = CompressMode.Percent, Percent = 50 }));
        Assert.Null(CompressPlanner.TargetBytes(629_145_600, new CompressOptions { Mode = CompressMode.Quality }));
    }

    [Fact]
    public void Quality_mode_uses_crf_and_frame_rate_limit()
    {
        var o = new CompressOptions { Mode = CompressMode.Quality, Strength = CompressStrength.Strong, MaxFrameRate = FrameRatePreset.Fps30 };
        var v = CompressPlanner.Video(Clip, o, []);
        Assert.Equal(VideoQualityMode.Crf, v.QualityMode);
        Assert.Equal(30, v.Crf);
        Assert.Equal(FrameRatePreset.Fps30, v.FrameRate);
        Assert.Equal(VideoContainer.Mp4, v.Container);
    }

    [Fact]
    public void Two_pass_writes_statistics_first()
    {
        var o = new CompressOptions { Mode = CompressMode.TargetSize, TargetMb = 50 };
        var command = FfmpegCommands.Video(Clip, "in.mp4", "out.tmp.mp4", CompressPlanner.Video(Clip, o, []));
        var (p1, p2) = FfmpegCommands.TwoPass(command, "pass");
        var l1 = string.Join(" ", p1.Args);
        var l2 = string.Join(" ", p2.Args);
        Assert.Contains("-pass 1 -passlogfile pass -an -sn -f null -", l1);
        Assert.EndsWith("-pass 2 -passlogfile pass out.tmp.mp4", l2);
        Assert.DoesNotContain("out.tmp.mp4", l1);
    }

    [Fact]
    public void Audio_target_snaps_to_standard_bitrate()
    {
        var song = FfprobeService.Parse(@"C:\a\song.flac", """
            {"streams":[{"index":0,"codec_name":"flac","codec_type":"audio","sample_rate":"44100","channels":2}],
             "format":{"duration":"240","bit_rate":"900000","size":"27000000"}}
            """);
        var a = CompressPlanner.Audio(song, new CompressOptions { Mode = CompressMode.TargetSize, TargetMb = 5, AudioFormat = AudioFormat.Mp3 });
        Assert.Equal(160, a.BitrateKbps);
    }

    [Fact]
    public void Loudness_and_silence_filters_are_chained()
    {
        var chain = FfmpegCommands.AudioChain("afade=t=in:st=0:d=1", new AudioOptions { TrimSilence = true, Loudness = LoudnessTarget.Lufs16 });
        Assert.StartsWith("afade", chain);
        Assert.Contains("areverse", chain);
        Assert.EndsWith("loudnorm=I=-16:TP=-1.5:LRA=11", chain);
    }

    [Fact]
    public void Loudness_keeps_the_source_sample_rate()
    {
        var song = FfprobeService.Parse(@"C:\a\song.mp3", """
            {"streams":[{"index":0,"codec_name":"mp3","codec_type":"audio","sample_rate":"44100","channels":2,"bit_rate":"192000"}],
             "format":{"duration":"100","bit_rate":"192000"}}
            """);
        var line = string.Join(" ", FfmpegCommands.Audio(song, "in.mp3", "out.wav", new AudioOptions { Format = AudioFormat.Wav, Loudness = LoudnessTarget.Lufs23 }).Args);
        Assert.Contains("loudnorm=I=-23", line);
        Assert.Contains("-ar 44100", line);
    }

    [Theory]
    [InlineData(1000, PowerOfTwo.Nearest, 1024)]
    [InlineData(700, PowerOfTwo.Nearest, 512)]
    [InlineData(700, PowerOfTwo.Up, 1024)]
    [InlineData(1000, PowerOfTwo.Down, 512)]
    [InlineData(512, PowerOfTwo.Up, 512)]
    [InlineData(300, PowerOfTwo.Off, 300)]
    public void Power_of_two_sides(int value, PowerOfTwo mode, int expected) =>
        Assert.Equal(expected, FormatCatalog.PowerOfTwoSide(value, mode));

    [Fact]
    public void Game_formats_have_extensions()
    {
        Assert.Equal("tga", FormatCatalog.Extension(ImageFormat.Tga, "png"));
        Assert.Equal(ImageFormat.Dds, FormatCatalog.ImageFormatFromExtension(".DDS"));
        Assert.True(FormatCatalog.SupportsAlpha(ImageFormat.Dds));
    }

    [Fact]
    public void Video_target_size_mode_derives_the_bitrate_from_the_source()
    {
        var o = new VideoOptions { QualityMode = VideoQualityMode.TargetSize, TargetPercent = 50, Audio = VideoAudioMode.Aac, AudioBitrateKbps = 128 };
        var resolved = CompressPlanner.ResolveTargetSize(Clip, o);
        Assert.Equal(VideoQualityMode.Bitrate, resolved.QualityMode);
        // 8200 kbps source × 50 % × reserve − 128 kbps audio
        Assert.True(resolved.BitrateKbps is > 3700 and < 4000, resolved.BitrateKbps.ToString());
        var line = string.Join(" ", FfmpegCommands.Video(Clip, "in.mp4", "out.mp4", o).Args);
        Assert.Contains("-b:v " + resolved.BitrateKbps + "k", line);
        Assert.Equal((long?)314_572_800, Core.Estimation.SizeEstimator.Video(Clip, o));
    }

    [Fact]
    public void Every_enum_value_has_a_label()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Data", "Strings.cs.txt");
        var text = File.ReadAllText(path);
        var en = text[..text.IndexOf("De = new", StringComparison.Ordinal)];
        var keys = Regex.Matches(en, "\\[\"([\\w\\.]+)\"\\]").Select(m => m.Groups[1].Value).ToHashSet();
        var missing = typeof(CompressOptions).Assembly.GetTypes()
            .Where(t => t.IsEnum && t.Namespace == "CONVERTit.Core.Options")
            .SelectMany(t => Enum.GetNames(t).Select(n => t.Name + "." + n))
            .Where(k => !keys.Contains(k))
            .ToList();
        Assert.Empty(missing);
    }
}
