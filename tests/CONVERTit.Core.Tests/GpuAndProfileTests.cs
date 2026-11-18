using CONVERTit.Core.Catalog;
using CONVERTit.Core.Ffmpeg;
using CONVERTit.Core.Machine;
using CONVERTit.Core.Media;
using CONVERTit.Core.Options;
using Xunit;

namespace CONVERTit.Core.Tests;

public class GpuAndProfileTests
{
    private static MediaInfo Clip(string codec = "h264", string pix = "yuv420p", string transfer = "", string field = "progressive", int width = 1920, int height = 1080) => new()
    {
        Path = @"C:\v\clip.mkv",
        Kind = MediaKind.Video,
        DurationSeconds = 60,
        Video =
        [
            new VideoStreamInfo
            {
                Index = 0, Codec = codec, Width = width, Height = height, PixelFormat = pix, Fps = 25,
                ColorTransfer = transfer, ColorPrimaries = transfer.Length > 0 ? "bt2020" : "", ColorSpace = transfer.Length > 0 ? "bt2020nc" : "",
                FieldOrder = field
            }
        ]
    };

    private static string Line(FfmpegCommand c) => string.Join(" ", c.Args);

    [Fact]
    public void Hardware_decoding_adds_hwaccel_before_the_input()
    {
        var c = FfmpegCommands.Video(Clip(), "in.mkv", "out.mp4", new VideoOptions(), environment: new EncodeEnvironment { HardwareDecode = true });
        Assert.StartsWith("-hwaccel auto -i in.mkv", Line(c));
        Assert.True(c.UsesHardwareDecode);
        Assert.False(c.UsesTurbo);
    }

    [Fact]
    public void Hardware_decoding_skips_codecs_gpus_do_not_decode()
    {
        var c = FfmpegCommands.Video(Clip(codec: "prores", pix: "yuv422p10le"), "in.mov", "out.mp4", new VideoOptions(),
            environment: new EncodeEnvironment { HardwareDecode = true });
        Assert.StartsWith("-i in.mov", Line(c));
    }

    [Fact]
    public void Turbo_keeps_decode_scale_and_encode_on_the_nvidia_gpu()
    {
        var o = new VideoOptions { Codec = VideoCodec.H264, Acceleration = Acceleration.Nvidia, Resolution = ResolutionPreset.P720 };
        var c = FfmpegCommands.Video(Clip(), "in.mkv", "out.mp4", o, environment: new EncodeEnvironment { GpuTurbo = true, HardwareDecode = true });
        var line = Line(c);
        Assert.StartsWith("-hwaccel cuda -hwaccel_output_format cuda -i in.mkv", line);
        Assert.Contains("-vf scale_cuda=1280:720:interp_algo=lanczos", line);
        Assert.DoesNotContain("-pix_fmt", line);
        Assert.True(c.UsesTurbo);
    }

    [Fact]
    public void Turbo_falls_back_to_cpu_filters_when_effects_are_needed()
    {
        var o = new VideoOptions { Codec = VideoCodec.H264, Acceleration = Acceleration.Intel, FrameRate = FrameRatePreset.Fps30 };
        var c = FfmpegCommands.Video(Clip(), "in.mkv", "out.mp4", o, environment: new EncodeEnvironment { GpuTurbo = true, HardwareDecode = true });
        Assert.False(c.UsesTurbo);
        Assert.StartsWith("-hwaccel auto", Line(c));
        Assert.Contains("fps=30", Line(c));
    }

    [Fact]
    public void Hdr_source_is_tone_mapped_for_8_bit_targets()
    {
        var c = FfmpegCommands.Video(Clip(codec: "hevc", pix: "yuv420p10le", transfer: "smpte2084"), "in.mkv", "out.mp4", new VideoOptions { Codec = VideoCodec.H264 });
        var line = Line(c);
        Assert.Contains("tonemap=tonemap=hable", line);
        Assert.Contains("-color_trc bt709", line);
        Assert.Contains("WarnHdrToneMapped", c.Warnings);
    }

    [Fact]
    public void Hdr_source_keeps_hdr_for_10_bit_hevc()
    {
        var o = new VideoOptions { Codec = VideoCodec.H265, Compatible8Bit = false };
        var line = Line(FfmpegCommands.Video(Clip(codec: "hevc", pix: "yuv420p10le", transfer: "smpte2084"), "in.mkv", "out.mkv", o));
        Assert.DoesNotContain("tonemap", line);
        Assert.Contains("-color_primaries bt2020 -color_trc smpte2084 -colorspace bt2020nc", line);
    }

    [Fact]
    public void Interlaced_source_is_deinterlaced()
    {
        var c = FfmpegCommands.Video(Clip(codec: "mpeg2video", field: "tt", width: 720, height: 576), "in.vob", "out.mp4", new VideoOptions());
        Assert.Contains("bwdif=mode=send_frame:parity=auto:deint=interlaced", Line(c));
        var off = FfmpegCommands.Video(Clip(codec: "mpeg2video", field: "tt", width: 720, height: 576), "in.vob", "out.mp4", new VideoOptions(),
            environment: new EncodeEnvironment { AutoDeinterlace = false });
        Assert.DoesNotContain("bwdif", Line(off));
    }

    [Fact]
    public void Hardware_quality_scales_match_the_encoders()
    {
        Assert.Equal(26, FormatCatalog.HardwareQuality("h264_nvenc", VideoCodec.H264, 23));
        Assert.Equal(54 - 3, FormatCatalog.HardwareQuality("h264_nvenc", VideoCodec.H264, 51));
        Assert.True(FormatCatalog.HardwareQuality("av1_amf", VideoCodec.Av1, 32) > 100);
        Assert.InRange(FormatCatalog.HardwareQuality("av1_qsv", VideoCodec.Av1, 63), 1, 51);
    }

    [Fact]
    public void Nvenc_crf_zero_is_lossless()
    {
        var o = new VideoOptions { Codec = VideoCodec.H264, Acceleration = Acceleration.Nvidia, QualityMode = VideoQualityMode.Crf, Crf = 0 };
        var line = Line(FfmpegCommands.Video(Clip(), "in.mkv", "out.mp4", o));
        Assert.Contains("-tune lossless", line);
        Assert.DoesNotContain("-cq", line);
    }

    [Fact]
    public void Threads_of_the_profile_reach_the_software_encoders()
    {
        var env = new EncodeEnvironment { Threads = 6 };
        Assert.Contains("-threads 6", Line(FfmpegCommands.Video(Clip(), "in.mkv", "out.mp4", new VideoOptions { Codec = VideoCodec.H264 }, environment: env)));
        Assert.Contains("log-level=error:pools=6", Line(FfmpegCommands.Video(Clip(), "in.mkv", "out.mp4", new VideoOptions { Codec = VideoCodec.H265 }, environment: env)));
    }

    [Fact]
    public void X265_two_pass_uses_relative_statistics_in_the_output_folder()
    {
        var o = new VideoOptions { Codec = VideoCodec.H265, QualityMode = VideoQualityMode.Bitrate, BitrateKbps = 3000 };
        var command = FfmpegCommands.Video(Clip(), "in.mkv", @"C:\out\a.tmp.mp4", o);
        var (p1, p2) = FfmpegCommands.TwoPass(command, Path.Combine("C:", "out", "a.pass"));
        Assert.Contains("log-level=error:pass=1:stats=a.pass.x265.log", Line(p1));
        Assert.Contains("log-level=error:pass=2:stats=a.pass.x265.log", Line(p2));
        Assert.Equal(Path.Combine("C:", "out"), p1.WorkingDirectory);
        Assert.EndsWith("-f null -", Line(p1));
    }

    [Fact]
    public void Auto_profile_follows_power_source_and_hardware()
    {
        Assert.Equal(PerformanceProfile.Eco, PerformancePlanner.Resolve(PerformanceProfile.Auto, new MachineInfo { HasBattery = true, OnBattery = true, LogicalCores = 8 }).Profile);
        Assert.Equal(PerformanceProfile.Balanced, PerformancePlanner.Resolve(PerformanceProfile.Auto, new MachineInfo { HasBattery = true, LogicalCores = 16 }).Profile);
        Assert.Equal(PerformanceProfile.Eco, PerformancePlanner.Resolve(PerformanceProfile.Auto, new MachineInfo { EnergySaver = true, LogicalCores = 16 }).Profile);
        Assert.Equal(PerformanceProfile.Maximum, PerformancePlanner.Resolve(PerformanceProfile.Auto, new MachineInfo { LogicalCores = 16 }).Profile);
        Assert.Equal(PerformanceProfile.Balanced, PerformancePlanner.Resolve(PerformanceProfile.Auto, new MachineInfo { LogicalCores = 4 }).Profile);
    }

    [Fact]
    public void Eco_profile_limits_threads_and_uses_ecoqos()
    {
        var plan = PerformancePlanner.Plan(PerformanceProfile.Eco, new MachineInfo { LogicalCores = 12 }, 1, 4, JobPriority.Normal, true);
        Assert.Equal(6, plan.Threads);
        Assert.Equal(1, plan.MediaParallel);
        Assert.True(plan.EcoQos);
        Assert.Equal(JobPriority.Idle, plan.Priority);
        Assert.False(plan.GpuTurbo);
    }
}
