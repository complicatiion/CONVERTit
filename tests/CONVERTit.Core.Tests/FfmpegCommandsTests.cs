using CONVERTit.Core.Ffmpeg;
using CONVERTit.Core.Media;
using CONVERTit.Core.Options;
using CONVERTit.Engines.Ffmpeg;
using Xunit;

namespace CONVERTit.Core.Tests;

public class FfmpegCommandsTests
{
    // 1080p H.264 with DTS 5.1, a text and a Blu-ray subtitle and a cover picture.
    private const string MovieJson = """
        {"streams":[
          {"index":0,"codec_name":"h264","codec_type":"video","width":1920,"height":1080,"pix_fmt":"yuv420p","avg_frame_rate":"30000/1001","bit_rate":"8000000"},
          {"index":1,"codec_name":"dts","codec_type":"audio","sample_rate":"48000","channels":6,"bit_rate":"1536000","tags":{"language":"eng"}},
          {"index":2,"codec_name":"subrip","codec_type":"subtitle"},
          {"index":3,"codec_name":"hdmv_pgs_subtitle","codec_type":"subtitle"},
          {"index":4,"codec_name":"mjpeg","codec_type":"video","width":600,"height":600,"avg_frame_rate":"0/0","disposition":{"attached_pic":1}}],
         "format":{"format_name":"matroska,webm","duration":"120.5","bit_rate":"9600000","size":"144000000"}}
        """;

    private const string SongJson = """
        {"streams":[
          {"index":0,"codec_name":"mp3","codec_type":"audio","sample_rate":"44100","channels":2,"bit_rate":"128000"},
          {"index":1,"codec_name":"mjpeg","codec_type":"video","width":500,"height":500,"avg_frame_rate":"0/0","disposition":{"attached_pic":1}}],
         "format":{"duration":"200","bit_rate":"130000"}}
        """;

    private static MediaInfo Movie => FfprobeService.Parse(@"C:\v\movie.mkv", MovieJson);

    private static MediaInfo Song => FfprobeService.Parse(@"C:\a\song.mp3", SongJson);

    private static string Line(FfmpegCommand c) => string.Join(" ", c.Args);

    [Fact]
    public void Probe_detects_streams_and_cover()
    {
        var info = Movie;
        Assert.Equal(MediaKind.Video, info.Kind);
        Assert.Equal(0, info.MainVideo!.Index);
        Assert.Equal(4, info.CoverArt!.Index);
        Assert.Equal(29.97, info.MainVideo.Fps, 2);
        Assert.Equal(MediaKind.Audio, Song.Kind);
    }

    [Fact]
    public void Mp4_default_reencodes_dts_and_drops_bitmap_subtitles()
    {
        var c = FfmpegCommands.Video(Movie, "in.mkv", "out.mp4", new VideoOptions());
        var line = Line(c);
        Assert.Contains("-c:a aac", line);
        Assert.Contains("-map 0:2 -c:s mov_text", line);
        Assert.DoesNotContain("0:3", line);
        Assert.Contains("WarnBitmapSubtitlesDropped", c.Warnings);
        Assert.Contains("-movflags +faststart", line);
        Assert.Contains("-crf 23", line);
    }

    [Fact]
    public void Resolution_aspect_fps_and_fades_build_one_filter_chain()
    {
        var o = new VideoOptions
        {
            Resolution = ResolutionPreset.P720, Aspect = AspectPreset.R9x16, ScaleMode = ScaleMode.Fill,
            FrameRate = FrameRatePreset.Fps2997, Codec = VideoCodec.H265, Acceleration = Acceleration.Nvidia,
            QualityMode = VideoQualityMode.Crf, Crf = 28
        };
        var c = FfmpegCommands.Video(Movie, "in.mkv", "out.mp4", o, new ClipRange(10, 5), 1, 1);
        var line = Line(c);
        Assert.StartsWith("-ss 10 -t 5 -i in.mkv", line);
        Assert.Contains("scale=720:1280:force_original_aspect_ratio=increase:flags=lanczos,crop=720:1280,setsar=1,fps=30000/1001,fade=t=in:st=0:d=1,fade=t=out:st=4:d=1", line);
        Assert.Contains("-c:v hevc_nvenc -rc vbr -cq 28 -b:v 0", line);
        Assert.Contains("-tag:v hvc1", line);
        Assert.Equal(5, c.ExpectedSeconds);
    }

    [Fact]
    public void Webm_forces_vp9_and_opus()
    {
        var o = new VideoOptions { Container = VideoContainer.WebM, Codec = VideoCodec.H264 };
        var line = Line(FfmpegCommands.Video(Movie, "in.mkv", "out.webm", o));
        Assert.Contains("-c:v libvpx-vp9", line);
        Assert.Contains("-b:v 0", line);
        Assert.Contains("-c:a libopus", line);
    }

    [Fact]
    public void Boomerang_doubles_length_and_removes_audio()
    {
        var c = FfmpegCommands.Video(Movie, "in.mkv", "out.mp4", new VideoOptions(), new ClipRange(3, 2), boomerang: true);
        Assert.Equal(4, c.ExpectedSeconds);
        Assert.Contains("reverse", Line(c));
        Assert.Contains("-an", c.Args);
    }

    [Fact]
    public void Loop_with_target_length_computes_play_count()
    {
        var c = FfmpegCommands.Loop("seg.mp4", "out.mp4", 7, 1, 60);
        Assert.Equal(new[] { "-stream_loop", "8" }, c.Args.Take(2).ToArray());
        Assert.Contains("-t", c.Args);
        Assert.Equal(60, c.ExpectedSeconds);
    }

    [Fact]
    public void Remux_rejects_dts_in_mp4_but_allows_mkv()
    {
        var ex = Assert.Throws<ConversionException>(() => FfmpegCommands.Remux(Movie, "in.mkv", "out.mp4", new RemuxOptions()));
        Assert.Equal("ErrRemuxAudio", ex.Key);
        var mkv = FfmpegCommands.Remux(Movie, "in.mkv", "out.mkv", new RemuxOptions { Container = VideoContainer.Mkv });
        Assert.Contains("-c:s copy", Line(mkv));
    }

    [Fact]
    public void Gif_uses_palette_and_loop_count()
    {
        var c = FfmpegCommands.Animation(Movie, "in.mkv", "out.gif", new GifOptions { LoopCount = 3 });
        var line = Line(c);
        Assert.Contains("palettegen", line);
        Assert.Contains("paletteuse=dither=bayer", line);
        Assert.Contains("-loop 2", line);
    }

    [Fact]
    public void Audio_bitrate_is_capped_at_lossy_source()
    {
        var c = FfmpegCommands.Audio(Song, "in.mp3", "out.m4a", new AudioOptions { Format = AudioFormat.Aac, BitrateKbps = 256 });
        Assert.Contains("-b:a 128k", Line(c));
        Assert.Contains("WarnBitrateCapped", c.Warnings);
        Assert.Contains("attached_pic", Line(c));
    }

    [Fact]
    public void Audio_vbr_samplerate_and_mono()
    {
        var o = new AudioOptions { Mp3Mode = BitrateMode.Vbr, Mp3VbrLevel = 2, SampleRate = 48000, Channels = ChannelMode.Mono };
        var line = Line(FfmpegCommands.Audio(Song, "in.mp3", "out.mp3", o));
        Assert.Contains("-q:a 2", line);
        Assert.Contains("-ar 48000", line);
        Assert.Contains("-ac 1", line);
    }

    [Fact]
    public void Opus_ignores_unsupported_sample_rate_and_fades()
    {
        var o = new AudioOptions { Format = AudioFormat.Opus, BitrateKbps = 96, SampleRate = 44100 };
        var c = FfmpegCommands.Audio(Song, "in.mp3", "out.opus", o, ClipRange.From(10, 40), 2, 3);
        var line = Line(c);
        Assert.DoesNotContain("-ar", c.Args);
        Assert.Contains("afade=t=out:st=27:d=3", line);
        Assert.Equal(30, c.ExpectedSeconds);
    }

    [Fact]
    public void Lossy_to_lossless_warns()
    {
        var c = FfmpegCommands.Audio(Song, "in.mp3", "out.flac", new AudioOptions { Format = AudioFormat.Flac });
        Assert.Contains("WarnLossyToLossless", c.Warnings);
    }
}
