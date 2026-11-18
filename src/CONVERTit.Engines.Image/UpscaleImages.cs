using ImageMagick;

namespace CONVERTit.Engines.Image;

/// <summary>Pre- and post-processing around the ncnn upscalers (they read and write only PNG/JPG/WebP).</summary>
public static class UpscaleImages
{
    /// <summary>Formats the ncnn tools read directly.</summary>
    public static bool ToolCanRead(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".webp";

    /// <summary>Converts any readable image to PNG for the tool (first frame, alpha kept).</summary>
    public static void ToPng(string input, string output)
    {
        using var image = new MagickImage(input);
        image.AutoOrient();
        image.Format = MagickFormat.Png32;
        image.Write(output);
    }

    public static (int Width, int Height) Size(string path)
    {
        var info = new MagickImageInfo(path);
        return ((int)info.Width, (int)info.Height);
    }

    /// <summary>
    /// Writes the tool's result in the requested format and, when the model's native factor was larger than the
    /// requested one (x4 model, ×2 wanted), scales it down to exactly source × factor with a Lanczos filter.
    /// </summary>
    public static void Finish(string upscaled, string output, int sourceWidth, int sourceHeight, int factor, string format, int quality)
    {
        using var image = new MagickImage(upscaled);
        var width = (uint)Math.Max(1, sourceWidth * factor);
        var height = (uint)Math.Max(1, sourceHeight * factor);
        if (image.Width != width || image.Height != height)
        {
            image.FilterType = FilterType.Lanczos;
            image.Resize(new MagickGeometry(width, height) { IgnoreAspectRatio = true });
        }

        switch (format)
        {
            case "jpg":
                image.BackgroundColor = MagickColors.White;
                image.Alpha(AlphaOption.Remove);
                image.Quality = (uint)Math.Clamp(quality, 1, 100);
                image.Format = MagickFormat.Jpeg;
                break;
            case "webp":
                image.Quality = (uint)Math.Clamp(quality, 1, 100);
                image.Format = MagickFormat.WebP;
                break;
            default:
                image.Format = MagickFormat.Png;
                break;
        }

        image.Write(output);
    }
}
