using System.Globalization;
using CONVERTit.Core.Catalog;
using CONVERTit.Core.Options;

namespace CONVERTit.Core.Ffmpeg;

/// <summary>Output size of a video from resolution preset, aspect ratio and scale mode.</summary>
public static class VideoGeometry
{
    public readonly record struct Size(int Width, int Height);

    /// <summary>
    /// Target frame size, or null when the picture stays as it is.
    /// Presets describe the short side (1080p = 1920×1080 landscape, 1080×1920 portrait).
    /// </summary>
    public static Size? Compute(int sourceWidth, int sourceHeight, VideoOptions o)
    {
        if (sourceWidth <= 0 || sourceHeight <= 0)
        {
            // Unknown source size: only explicit sizes can be applied.
            return o.Resolution == ResolutionPreset.Custom ? Even(o.CustomWidth, o.CustomHeight) : null;
        }

        double sw = sourceWidth, sh = sourceHeight;
        var sourceAspect = sw / sh;

        if (o.Resolution == ResolutionPreset.Custom)
        {
            if (o.CustomWidth <= 0 || o.CustomHeight <= 0)
            {
                return null;
            }

            if (o.NoUpscale && o.CustomWidth > sw && o.CustomHeight > sh)
            {
                return null;
            }

            return Even(o.CustomWidth, o.CustomHeight);
        }

        var aspect = TargetAspect(o, sourceAspect);
        var aspectChanged = Math.Abs(aspect - sourceAspect) > 0.01;
        var shortSide = FormatCatalog.ShortSide(o.Resolution);

        if (shortSide == 0)
        {
            if (!aspectChanged)
            {
                return null;
            }

            // Same size class as the source: Fill crops inside it, Fit pads around it.
            double w, h;
            var wider = sourceAspect > aspect;
            if (o.ScaleMode == ScaleMode.Fit)
            {
                if (wider) { w = sw; h = sw / aspect; }
                else { h = sh; w = sh * aspect; }
            }
            else
            {
                if (wider) { h = sh; w = sh * aspect; }
                else { w = sw; h = sw / aspect; }
            }

            return Even(w, h);
        }

        if (o.NoUpscale && shortSide > Math.Min(sw, sh))
        {
            // Smaller source: keep its size, only the aspect may change.
            if (!aspectChanged)
            {
                return null;
            }

            shortSide = (int)Math.Min(sw, sh);
        }

        return aspect >= 1
            ? Even(shortSide * aspect, shortSide)
            : Even(shortSide, shortSide / aspect);
    }

    public static double TargetAspect(VideoOptions o, double sourceAspect)
    {
        if (o.Aspect == AspectPreset.Custom && o.CustomAspectWidth > 0 && o.CustomAspectHeight > 0)
        {
            return (double)o.CustomAspectWidth / o.CustomAspectHeight;
        }

        return FormatCatalog.AspectRatio(o.Aspect) ?? sourceAspect;
    }

    /// <summary>Scale/pad/crop filter for the computed size (empty when the size stays).</summary>
    public static string Filter(int sourceWidth, int sourceHeight, VideoOptions o)
    {
        var size = Compute(sourceWidth, sourceHeight, o);
        if (size is null)
        {
            return "";
        }

        var (w, h) = (size.Value.Width, size.Value.Height);
        var sourceAspect = sourceWidth > 0 && sourceHeight > 0 ? (double)sourceWidth / sourceHeight : (double)w / h;
        var aspectChanged = Math.Abs((double)w / h - sourceAspect) > 0.01;
        var mode = aspectChanged ? o.ScaleMode : ScaleMode.Stretch;

        return mode switch
        {
            ScaleMode.Fit =>
                $"scale={w}:{h}:force_original_aspect_ratio=decrease:force_divisible_by=2:flags=lanczos," +
                $"pad={w}:{h}:(ow-iw)/2:(oh-ih)/2:color={PadColor(o.PadColor)},setsar=1",
            ScaleMode.Fill =>
                $"scale={w}:{h}:force_original_aspect_ratio=increase:flags=lanczos,crop={w}:{h},setsar=1",
            _ => $"scale={w}:{h}:flags=lanczos,setsar=1"
        };
    }

    /// <summary>"#1E1E1E" → "0x1E1E1E" (FFmpeg color syntax); invalid values become black.</summary>
    public static string PadColor(string? hex)
    {
        var value = (hex ?? "").Trim().TrimStart('#');
        if (value.Length == 6 && int.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _))
        {
            return "0x" + value.ToUpperInvariant();
        }

        return "black";
    }

    private static Size Even(double w, double h) =>
        new(Math.Max(2, (int)Math.Round(w / 2d) * 2), Math.Max(2, (int)Math.Round(h / 2d) * 2));
}
