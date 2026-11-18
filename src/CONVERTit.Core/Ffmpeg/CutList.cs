namespace CONVERTit.Core.Ffmpeg;

/// <summary>Part of the timeline between two cuts; removed parts are left out of the result.</summary>
public sealed class TrimSegment
{
    public double Start { get; set; }

    public double End { get; set; }

    public bool Removed { get; set; }

    public double Length => Math.Max(0, End - Start);
}

/// <summary>
/// Multi-cut of the trim pages: cuts split the timeline into segments, segments can be removed or restored, cut
/// lines can be moved. The kept segments are joined (or written as separate files). Pure functions (unit-tested).
/// </summary>
public static class CutList
{
    /// <summary>Segments shorter than this are not created (prevents double cuts at the same spot).</summary>
    public const double MinLength = 0.05;

    /// <summary>Cuts at <paramref name="at"/>; the first cut creates the segment list for the whole file.</summary>
    public static bool Split(List<TrimSegment> segments, double at, double duration)
    {
        if (duration <= 0 || at <= MinLength || at >= duration - MinLength)
        {
            return false;
        }

        if (segments.Count == 0)
        {
            segments.Add(new TrimSegment { Start = 0, End = duration });
        }

        var index = IndexAt(segments, at);
        if (index < 0)
        {
            return false;
        }

        var segment = segments[index];
        if (at - segment.Start < MinLength || segment.End - at < MinLength)
        {
            return false;
        }

        segments.Insert(index + 1, new TrimSegment { Start = at, End = segment.End, Removed = segment.Removed });
        segment.End = at;
        return true;
    }

    /// <summary>Index of the segment that contains <paramref name="time"/> (-1 when none).</summary>
    public static int IndexAt(IReadOnlyList<TrimSegment> segments, double time)
    {
        for (var i = 0; i < segments.Count; i++)
        {
            if (time >= segments[i].Start && (time < segments[i].End || i == segments.Count - 1 && time <= segments[i].End))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Moves cut number <paramref name="cut"/> (between segment cut-1 and cut) within its neighbours.</summary>
    public static double MoveCut(List<TrimSegment> segments, int cut, double to)
    {
        if (cut <= 0 || cut >= segments.Count)
        {
            return to;
        }

        var left = segments[cut - 1];
        var right = segments[cut];
        var clamped = Math.Clamp(to, left.Start + MinLength, right.End - MinLength);
        left.End = clamped;
        right.Start = clamped;
        return clamped;
    }

    /// <summary>Removes cut number <paramref name="cut"/>: the two segments become one (kept if either was kept).</summary>
    public static void Join(List<TrimSegment> segments, int cut)
    {
        if (cut <= 0 || cut >= segments.Count)
        {
            return;
        }

        var left = segments[cut - 1];
        var right = segments[cut];
        left.End = right.End;
        left.Removed = left.Removed && right.Removed;
        segments.RemoveAt(cut);
        if (segments.Count == 1 && !segments[0].Removed)
        {
            segments.Clear();
        }
    }

    /// <summary>Keeps only segment <paramref name="index"/>.</summary>
    public static void KeepOnly(List<TrimSegment> segments, int index)
    {
        for (var i = 0; i < segments.Count; i++)
        {
            segments[i].Removed = i != index;
        }
    }

    /// <summary>
    /// Time ranges of the result: kept segments, neighbours joined into one range. Without cuts the whole file
    /// (or nothing when the duration is unknown).
    /// </summary>
    public static List<ClipRange> KeptRanges(IReadOnlyList<TrimSegment> segments, double duration)
    {
        var ranges = new List<ClipRange>();
        if (segments.Count == 0)
        {
            if (duration > 0)
            {
                ranges.Add(new ClipRange(0, duration));
            }

            return ranges;
        }

        double? start = null;
        double end = 0;
        foreach (var segment in segments)
        {
            if (segment.Removed || segment.Length < MinLength)
            {
                if (start is { } s)
                {
                    ranges.Add(new ClipRange(s, end - s));
                    start = null;
                }

                continue;
            }

            if (start is not null && Math.Abs(segment.Start - end) < 0.001)
            {
                end = segment.End;
                continue;
            }

            if (start is { } open)
            {
                ranges.Add(new ClipRange(open, end - open));
            }

            start = segment.Start;
            end = segment.End;
        }

        if (start is { } last)
        {
            ranges.Add(new ClipRange(last, end - last));
        }

        return ranges;
    }

    /// <summary>Length of the result in seconds.</summary>
    public static double KeptLength(IReadOnlyList<TrimSegment> segments, double duration) =>
        KeptRanges(segments, duration).Sum(r => r.Duration ?? 0);
}
