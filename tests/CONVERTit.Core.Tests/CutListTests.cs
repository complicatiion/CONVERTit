using CONVERTit.Core.Ffmpeg;
using Xunit;

namespace CONVERTit.Core.Tests;

public class CutListTests
{
    [Fact]
    public void First_cut_creates_two_segments()
    {
        var s = new List<TrimSegment>();
        Assert.True(CutList.Split(s, 10, 60));
        Assert.Equal(2, s.Count);
        Assert.Equal(10, s[0].End);
        Assert.Equal(10, s[1].Start);
        Assert.Equal(60, s[1].End);
    }

    [Fact]
    public void Cuts_at_the_edges_or_twice_at_the_same_spot_are_ignored()
    {
        var s = new List<TrimSegment>();
        Assert.False(CutList.Split(s, 0, 60));
        Assert.False(CutList.Split(s, 60, 60));
        Assert.True(CutList.Split(s, 20, 60));
        Assert.False(CutList.Split(s, 20.01, 60));
        Assert.Equal(2, s.Count);
    }

    [Fact]
    public void Kept_ranges_skip_removed_segments_and_join_neighbours()
    {
        var s = new List<TrimSegment>();
        CutList.Split(s, 10, 60);
        CutList.Split(s, 20, 60);
        CutList.Split(s, 40, 60);
        s[1].Removed = true; // 10–20 out
        var ranges = CutList.KeptRanges(s, 60);
        Assert.Equal(2, ranges.Count);
        Assert.Equal(0, ranges[0].Start);
        Assert.Equal(10, ranges[0].Duration);
        Assert.Equal(20, ranges[1].Start);
        Assert.Equal(40, ranges[1].Duration); // 20–40 and 40–60 joined
        Assert.Equal(50, CutList.KeptLength(s, 60));
    }

    [Fact]
    public void Moving_a_cut_stays_between_its_neighbours()
    {
        var s = new List<TrimSegment>();
        CutList.Split(s, 10, 60);
        CutList.Split(s, 20, 60);
        Assert.Equal(20 - CutList.MinLength, CutList.MoveCut(s, 1, 30), 3);
        Assert.Equal(s[0].End, s[1].Start);
    }

    [Fact]
    public void Join_removes_a_cut_and_keep_only_marks_the_rest_removed()
    {
        var s = new List<TrimSegment>();
        CutList.Split(s, 10, 60);
        CutList.Split(s, 20, 60);
        CutList.KeepOnly(s, 1);
        Assert.True(s[0].Removed);
        Assert.False(s[1].Removed);
        Assert.True(s[2].Removed);
        CutList.Join(s, 2);
        Assert.Equal(2, s.Count);
        Assert.False(s[1].Removed);
        Assert.Equal(60, s[1].End);
    }

    [Fact]
    public void Concat_list_quotes_windows_paths()
    {
        var list = FfmpegCommands.ConcatList([@"C:\v\it's.mp4", @"C:\v\b.mp4"]);
        Assert.Equal("file 'C:/v/it'\\''s.mp4'\nfile 'C:/v/b.mp4'\n", list);
        var c = FfmpegCommands.Concat("list.txt", "out.mp4", 12);
        Assert.Equal("-f concat -safe 0 -i list.txt -map 0 -c copy -movflags +faststart out.mp4", string.Join(" ", c.Args));
    }
}
