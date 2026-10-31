using System.Globalization;

namespace CONVERTit.Core.Ffmpeg;

/// <summary>Snapshot of FFmpeg's machine-readable progress (-progress pipe:1).</summary>
public sealed record FfmpegProgress(double OutSeconds, double Speed, double Fps, long TotalSize, bool Ended);

/// <summary>
/// Collects the key=value blocks FFmpeg writes with <c>-progress pipe:1 -nostats</c>.
/// A block ends with <c>progress=continue</c> or <c>progress=end</c>.
/// </summary>
public sealed class ProgressParser
{
    private double _outSeconds;
    private double _speed;
    private double _fps;
    private long _size;

    /// <summary>Feeds one line; returns a snapshot when a block is complete.</summary>
    public FfmpegProgress? Feed(string? line)
    {
        if (string.IsNullOrEmpty(line))
        {
            return null;
        }

        var eq = line.IndexOf('=');
        if (eq <= 0)
        {
            return null;
        }

        var key = line[..eq].Trim();
        var value = line[(eq + 1)..].Trim();
        switch (key)
        {
            // out_time_ms is microseconds as well (historic FFmpeg naming bug); out_time_us is preferred.
            case "out_time_us":
            case "out_time_ms":
                if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var micro) && micro >= 0)
                {
                    _outSeconds = micro / 1_000_000d;
                }

                break;
            case "out_time":
                if (Timecode.TryParse(value, out var t) && t >= 0)
                {
                    _outSeconds = Math.Max(_outSeconds, t);
                }

                break;
            case "speed":
                if (double.TryParse(value.TrimEnd('x'), NumberStyles.Float, CultureInfo.InvariantCulture, out var speed))
                {
                    _speed = speed;
                }

                break;
            case "fps":
                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var fps))
                {
                    _fps = fps;
                }

                break;
            case "total_size":
                if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var size))
                {
                    _size = size;
                }

                break;
            case "progress":
                return new FfmpegProgress(_outSeconds, _speed, _fps, _size, value == "end");
        }

        return null;
    }

    /// <summary>Fraction 0…1 for a known expected duration.</summary>
    public static double Fraction(FfmpegProgress progress, double expectedSeconds)
    {
        if (progress.Ended)
        {
            return 1;
        }

        return expectedSeconds > 0 ? Math.Clamp(progress.OutSeconds / expectedSeconds, 0, 0.999) : 0;
    }
}

/// <summary>Time values as typed by users and written by FFmpeg.</summary>
public static class Timecode
{
    /// <summary>Accepts "90", "90.5", "1:30", "01:30.250", "1:02:03.5" (also with comma as decimal separator).</summary>
    public static bool TryParse(string? text, out double seconds)
    {
        seconds = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = text.Trim().Replace(',', '.').Split(':');
        if (parts.Length > 3)
        {
            return false;
        }

        double total = 0;
        foreach (var part in parts)
        {
            if (!double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || value < 0)
            {
                return false;
            }

            total = total * 60 + value;
        }

        seconds = total;
        return true;
    }

    /// <summary>"0:01:30.250" style (hours only when needed).</summary>
    public static string Format(double seconds, bool milliseconds = true)
    {
        if (double.IsNaN(seconds) || seconds < 0)
        {
            seconds = 0;
        }

        var t = TimeSpan.FromSeconds(seconds);
        var head = t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes:00}:{t.Seconds:00}";
        return milliseconds ? $"{head}.{t.Milliseconds:000}" : head;
    }
}
