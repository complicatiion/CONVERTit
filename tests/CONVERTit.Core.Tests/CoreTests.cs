using System.Text.RegularExpressions;
using CONVERTit.Core.Ffmpeg;
using CONVERTit.Core.Files;
using CONVERTit.Core.Media;
using CONVERTit.Core.Options;
using CONVERTit.Engines.Ffmpeg;
using Xunit;

namespace CONVERTit.Core.Tests;

public class GeometryTests
{
    [Theory]
    [InlineData(1920, 1080, ResolutionPreset.P720, AspectPreset.Original, 1280, 720)]
    [InlineData(1080, 1920, ResolutionPreset.P720, AspectPreset.Original, 720, 1280)]
    [InlineData(1920, 1080, ResolutionPreset.P1080, AspectPreset.R9x16, 1080, 1920)]
    [InlineData(640, 480, ResolutionPreset.P1080, AspectPreset.R16x9, 854, 480)]
    public void Presets_describe_the_short_side(int sw, int sh, ResolutionPreset res, AspectPreset aspect, int w, int h)
    {
        var size = VideoGeometry.Compute(sw, sh, new VideoOptions { Resolution = res, Aspect = aspect });
        Assert.True(size.HasValue);
        Assert.Equal(w, size!.Value.Width);
        Assert.Equal(h, size.Value.Height);
    }

    [Fact]
    public void Original_without_changes_needs_no_scaling()
    {
        Assert.Null(VideoGeometry.Compute(1920, 1080, new VideoOptions()));
        Assert.Equal("", VideoGeometry.Filter(1920, 1080, new VideoOptions()));
    }

    [Fact]
    public void No_upscale_keeps_small_sources()
    {
        Assert.Null(VideoGeometry.Compute(640, 360, new VideoOptions { Resolution = ResolutionPreset.P1080 }));
    }

    [Fact]
    public void Fit_pads_with_color()
    {
        var filter = VideoGeometry.Filter(1920, 1080, new VideoOptions { Aspect = AspectPreset.R1x1, ScaleMode = ScaleMode.Fit, PadColor = "#112233" });
        Assert.Contains("pad=1920:1920", filter);
        Assert.Contains("color=0x112233", filter);
    }
}

public class ParserTests
{
    [Fact]
    public void Progress_block_is_reported_on_progress_line()
    {
        var parser = new ProgressParser();
        FfmpegProgress? last = null;
        foreach (var line in new[] { "frame=10", "fps=25.0", "out_time_us=5000000", "speed=2.5x", "progress=continue" })
        {
            last = parser.Feed(line) ?? last;
        }

        Assert.NotNull(last);
        Assert.Equal(5, last.OutSeconds);
        Assert.Equal(2.5, last.Speed);
        Assert.Equal(0.5, ProgressParser.Fraction(last, 10));
    }

    [Theory]
    [InlineData("90", 90)]
    [InlineData("1:30", 90)]
    [InlineData("01:02:03,5", 3723.5)]
    [InlineData("0:00.250", 0.25)]
    public void Timecodes_are_parsed(string text, double seconds)
    {
        Assert.True(Timecode.TryParse(text, out var value));
        Assert.Equal(seconds, value, 3);
    }

    [Fact]
    public void Encoder_list_is_parsed()
    {
        var set = HardwareProbe.ParseEncoders(" V....D libx264              libx264 H.264\n A....D aac   AAC\n V....D h264_nvenc  NVIDIA\n");
        Assert.Contains("libx264", set);
        Assert.Contains("h264_nvenc", set);
        Assert.Contains("aac", set);
    }
}

public class OutputTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cvt-tests-" + Guid.NewGuid().ToString("N")[..8]);

    public OutputTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "sub"));
        File.WriteAllText(Path.Combine(_root, "sub", "a.mp3"), "x");
        File.WriteAllText(Path.Combine(_root, "sub", "a.cvt-1a2b3c.mp3"), "x");
        File.WriteAllText(Path.Combine(_root, "b.png"), "x");
    }

    public void Dispose() => Directory.Delete(_root, true);

    [Fact]
    public void Folder_batch_mirrors_structure_and_reserves_names()
    {
        var resolver = new OutputPathResolver(new OutputOptions());
        var src = Path.Combine(_root, "sub", "a.mp3");
        Assert.Equal(Path.Combine(_root, "converted", "sub", "a.mp3"), resolver.Plan(src, _root, "mp3").Path);
        Assert.Equal(Path.Combine(_root, "converted", "sub", "a (2).mp3"), resolver.Plan(src, _root, "mp3").Path);
    }

    [Fact]
    public void Source_is_never_the_output()
    {
        var resolver = new OutputPathResolver(new OutputOptions { Destination = OutputDestination.SameFolder });
        var src = Path.Combine(_root, "sub", "a.mp3");
        Assert.Equal(Path.Combine(_root, "sub", "a_converted.mp3"), resolver.Plan(src, null, "mp3").Path);
    }

    [Fact]
    public void Skip_policy_marks_existing_files()
    {
        var resolver = new OutputPathResolver(new OutputOptions { Destination = OutputDestination.SameFolder, Conflict = ConflictPolicy.Skip, Suffix = "_x" });
        File.WriteAllText(Path.Combine(_root, "b_x.webp"), "x");
        Assert.True(resolver.Plan(Path.Combine(_root, "b.png"), null, "webp").Skip);
    }

    [Fact]
    public void Scanner_skips_temp_files_and_output_folder()
    {
        Directory.CreateDirectory(Path.Combine(_root, "converted"));
        File.WriteAllText(Path.Combine(_root, "converted", "old.mp3"), "x");
        var resolver = new OutputPathResolver(new OutputOptions());
        var files = FileScanner.Scan(_root, MediaKind.Audio, new SourceOptions(), resolver.ExcludedFolders(_root));
        Assert.Single(files);
        Assert.EndsWith("a.mp3", files[0]);
    }

    [Fact]
    public void Extension_list_is_parsed()
    {
        var set = MediaTypes.ParseExtensionList("mp3, .OGG *.m4a;flac");
        Assert.Equal(4, set.Count);
        Assert.Contains("ogg", set);
    }
}

public class StringsTests
{
    [Fact]
    public void German_has_every_english_key()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Data", "Strings.cs.txt");
        var text = File.ReadAllText(path);
        var split = text.IndexOf("De = new", StringComparison.Ordinal);
        Assert.True(split > 0);
        static HashSet<string> Keys(string part) =>
            Regex.Matches(part, "\\[\"([\\w\\.]+)\"\\]").Select(m => m.Groups[1].Value).ToHashSet();
        var en = Keys(text[..split]);
        var de = Keys(text[split..]);
        Assert.Empty(en.Except(de));
        Assert.Empty(de.Except(en));
    }
}
