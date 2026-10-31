using System.Text.Json;
using CONVERTit.Core.Catalog;
using CONVERTit.Core.Media;
using CONVERTit.Core.Options;
using ImageMagick;

namespace CONVERTit.Engines.Image;

/// <summary>
/// Image conversion with Magick.NET. All methods are synchronous and CPU-bound; the job queue runs them on worker
/// threads (image lane) and checks cancellation between frames and steps.
/// </summary>
public static class ImageEngine
{
    private static readonly Lazy<HashSet<ImageFormat>> Writable = new(() =>
    {
        var set = new HashSet<ImageFormat>();
        foreach (var (format, magick) in new[]
                 {
                     (ImageFormat.Png, MagickFormat.Png), (ImageFormat.Jpeg, MagickFormat.Jpeg), (ImageFormat.WebP, MagickFormat.WebP),
                     (ImageFormat.Avif, MagickFormat.Avif), (ImageFormat.Tiff, MagickFormat.Tiff), (ImageFormat.Bmp, MagickFormat.Bmp),
                     (ImageFormat.Gif, MagickFormat.Gif), (ImageFormat.Ico, MagickFormat.Ico),
                     (ImageFormat.Tga, MagickFormat.Tga), (ImageFormat.Dds, MagickFormat.Dds)
                 })
        {
            if (MagickNET.SupportedFormats.Any(f => f.Format == magick && f.SupportsWriting))
            {
                set.Add(format);
            }
        }

        return set;
    });

    /// <summary>ImageMagick version for the dependencies page.</summary>
    public static string Version
    {
        get
        {
            try
            {
                return MagickNET.Version;
            }
            catch
            {
                return "";
            }
        }
    }

    public static bool CanWrite(ImageFormat format)
    {
        try
        {
            return format == ImageFormat.Keep || Writable.Value.Contains(format);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Actual target format and extension ("Keep" falls back to PNG for formats that cannot be written).</summary>
    public static (ImageFormat Format, string Extension) ResolveTarget(ImageFormat requested, string sourcePath)
    {
        var sourceExt = MediaTypes.Extension(sourcePath);
        if (requested != ImageFormat.Keep)
        {
            return (requested, FormatCatalog.Extension(requested, sourceExt));
        }

        var same = FormatCatalog.ImageFormatFromExtension(sourceExt);
        return same != ImageFormat.Keep && CanWrite(same)
            ? (same, FormatCatalog.Extension(same, sourceExt) == "jpg" && sourceExt is "jpeg" or "jpe" or "jfif" ? sourceExt : FormatCatalog.Extension(same, sourceExt))
            : (ImageFormat.Png, "png");
    }

    // ================================================================ Info

    public static MediaInfo Info(string path)
    {
        var size = new FileInfo(path).Length;
        using var collection = new MagickImageCollection();
        collection.Ping(path);
        var first = collection.Count > 0 ? collection[0] : null;
        return new MediaInfo
        {
            Path = path,
            Kind = MediaKind.Image,
            FileSize = size,
            Format = first?.Format.ToString().ToUpperInvariant() ?? MediaTypes.Extension(path).ToUpperInvariant(),
            Image = new ImageDetails
            {
                Width = (int)(first?.Width ?? 0),
                Height = (int)(first?.Height ?? 0),
                HasAlpha = first?.HasAlpha ?? false,
                Frames = collection.Count,
                ColorSpace = first?.ColorSpace.ToString() ?? "",
                BitDepth = (int)(first?.Depth ?? 0),
                DpiX = first?.Density.X ?? 0
            }
        };
    }

    /// <summary>Downscaled PNG for previews (optionally on a solid background).</summary>
    public static byte[] Preview(string path, int maxSize)
    {
        using var image = new MagickImage();
        image.Read(path, new MagickReadSettings { FrameIndex = 0, FrameCount = 1 });
        image.AutoOrient();
        image.Thumbnail(new MagickGeometry((uint)maxSize, (uint)maxSize) { Greater = true });
        return image.ToByteArray(MagickFormat.Png);
    }

    /// <summary>Cover art for tags: auto-oriented, at most <paramref name="maxSide"/> px, JPEG quality 90, no metadata.</summary>
    public static byte[] CoverJpeg(byte[] source, int maxSide)
    {
        using var image = new MagickImage(source);
        image.AutoOrient();
        if (maxSide > 0)
        {
            image.FilterType = FilterType.Lanczos;
            image.Resize(new MagickGeometry((uint)maxSide, (uint)maxSide) { Greater = true });
        }

        if (image.HasAlpha)
        {
            image.BackgroundColor = MagickColors.White;
            image.Alpha(AlphaOption.Remove);
        }

        image.Strip();
        image.Quality = 90;
        return image.ToByteArray(MagickFormat.Jpeg);
    }

    /// <summary>"1000×1000 · JPEG" for the cover info line.</summary>
    public static string Describe(byte[] data)
    {
        try
        {
            var info = new MagickImageInfo(data);
            return $"{info.Width}×{info.Height} · {info.Format.ToString().ToUpperInvariant()}";
        }
        catch
        {
            return "";
        }
    }

    // ================================================================ Convert

    public static void Convert(string input, string output, ImageOptions o, ImageFormat target, CancellationToken cancellationToken)
    {
        var sourceExt = MediaTypes.Extension(input);
        var animated = o.KeepAnimation && FormatCatalog.SupportsAnimation(target) && sourceExt is "gif" or "webp" or "apng";
        if (animated)
        {
            using var frames = new MagickImageCollection(input);
            if (frames.Count > 1)
            {
                ConvertAnimation(frames, output, o, target, cancellationToken);
                return;
            }
        }

        using var image = new MagickImage();
        image.Read(input, new MagickReadSettings { FrameIndex = 0, FrameCount = 1 });
        cancellationToken.ThrowIfCancellationRequested();
        Prepare(image, o, target);
        cancellationToken.ThrowIfCancellationRequested();
        Write(image, output, o, target);
    }

    private static void Prepare(MagickImage image, ImageOptions o, ImageFormat target)
    {
        if (o.AutoOrient)
        {
            image.AutoOrient();
        }

        var keepAlpha = o.KeepTransparency && FormatCatalog.SupportsAlpha(target);
        Resize(image, o, keepAlpha);
        PowerOfTwoResize(image, o.PowerOfTwo);

        if (!keepAlpha && image.HasAlpha)
        {
            image.BackgroundColor = Color(o.BackgroundColor, MagickColors.White);
            image.Alpha(AlphaOption.Remove);
            image.Alpha(AlphaOption.Off);
        }

        if (o.StripMetadata)
        {
            image.Strip();
        }

        if (target == ImageFormat.Ico && (image.Width > 256 || image.Height > 256 || image.Width != image.Height))
        {
            // ICO frames are square and at most 256 px.
            image.FilterType = FilterType.Lanczos;
            image.Resize(new MagickGeometry(256, 256) { Greater = true });
            image.BackgroundColor = keepAlpha ? MagickColors.Transparent : Color(o.BackgroundColor, MagickColors.White);
            var side = Math.Max(image.Width, image.Height);
            image.Extent(side, side, Gravity.Center);
        }
    }

    private static void Resize(MagickImage image, ImageOptions o, bool keepAlpha)
    {
        image.FilterType = FilterType.Lanczos;
        switch (o.Resize)
        {
            case ResizeMode.MaxSize when o.Width > 0 || o.Height > 0:
            {
                var w = (uint)(o.Width > 0 ? o.Width : int.MaxValue / 4);
                var h = (uint)(o.Height > 0 ? o.Height : int.MaxValue / 4);
                image.Resize(new MagickGeometry(w, h) { Greater = o.NoUpscale });
                break;
            }
            case ResizeMode.Percent when o.Percent > 0 && !(o.NoUpscale && o.Percent > 100):
                image.Resize(new Percentage(o.Percent));
                break;
            case ResizeMode.Exact when o.Width > 0 && o.Height > 0:
            {
                var w = (uint)o.Width;
                var h = (uint)o.Height;
                switch (o.ExactMode)
                {
                    case ScaleMode.Fill:
                        image.Resize(new MagickGeometry(w, h) { FillArea = true });
                        image.Crop(w, h, Gravity.Center);
                        image.ResetPage();
                        break;
                    case ScaleMode.Stretch:
                        image.Resize(new MagickGeometry(w, h) { IgnoreAspectRatio = true });
                        break;
                    default:
                        image.Resize(new MagickGeometry(w, h) { Greater = o.NoUpscale });
                        image.BackgroundColor = keepAlpha ? MagickColors.Transparent : Color(o.BackgroundColor, MagickColors.White);
                        image.Extent(w, h, Gravity.Center);
                        break;
                }

                break;
            }
        }
    }

    /// <summary>Game textures: stretches each side to a power of two (the engines expect it for mipmaps and compression).</summary>
    private static void PowerOfTwoResize(MagickImage image, PowerOfTwo mode)
    {
        if (mode == PowerOfTwo.Off)
        {
            return;
        }

        var w = (uint)FormatCatalog.PowerOfTwoSide((int)image.Width, mode);
        var h = (uint)FormatCatalog.PowerOfTwoSide((int)image.Height, mode);
        if (w != image.Width || h != image.Height)
        {
            image.FilterType = FilterType.Lanczos;
            image.Resize(new MagickGeometry(w, h) { IgnoreAspectRatio = true });
        }
    }

    private static void Write(MagickImage image, string output, ImageOptions o, ImageFormat target)
    {
        var quality = (uint)Math.Clamp(o.Quality, 1, 100);
        switch (target)
        {
            case ImageFormat.Png:
                if (o.PngPalette)
                {
                    image.Quantize(new QuantizeSettings { Colors = (uint)Math.Clamp(o.PaletteColors, 2, 256), DitherMethod = DitherMethod.FloydSteinberg });
                    image.Settings.SetDefine(MagickFormat.Png, "compression-level", Math.Clamp(o.PngCompression, 0, 9).ToString());
                    image.Write(output, MagickFormat.Png8);
                }
                else
                {
                    image.Settings.SetDefine(MagickFormat.Png, "compression-level", Math.Clamp(o.PngCompression, 0, 9).ToString());
                    image.Write(output, MagickFormat.Png);
                }

                break;
            case ImageFormat.Jpeg:
                image.Quality = quality;
                image.Settings.SetDefine(MagickFormat.Jpeg, "sampling-factor", o.JpegChroma == ChromaSubsampling.S444 ? "4:4:4" : "4:2:0");
                image.Write(output, o.JpegProgressive ? MagickFormat.Pjpeg : MagickFormat.Jpeg);
                break;
            case ImageFormat.WebP:
                image.Quality = quality;
                image.Settings.SetDefine(MagickFormat.WebP, "lossless", o.WebpLossless ? "true" : "false");
                image.Settings.SetDefine(MagickFormat.WebP, "method", Math.Clamp(o.WebpMethod, 0, 6).ToString());
                image.Write(output, MagickFormat.WebP);
                break;
            case ImageFormat.Avif:
                image.Quality = quality;
                image.Settings.SetDefine(MagickFormat.Heic, "speed", Math.Clamp(o.AvifSpeed, 0, 9).ToString());
                image.Write(output, MagickFormat.Avif);
                break;
            case ImageFormat.Tiff:
                image.Settings.Compression = o.TiffCompression switch
                {
                    TiffCompression.Zip => CompressionMethod.Zip,
                    TiffCompression.None => CompressionMethod.NoCompression,
                    _ => CompressionMethod.LZW
                };
                image.Write(output, MagickFormat.Tiff);
                break;
            case ImageFormat.Bmp:
                image.Write(output, MagickFormat.Bmp);
                break;
            case ImageFormat.Gif:
                image.Quantize(new QuantizeSettings { Colors = 256, DitherMethod = o.GifDither ? DitherMethod.FloydSteinberg : DitherMethod.No });
                image.Write(output, MagickFormat.Gif);
                break;
            case ImageFormat.Ico:
                image.Write(output, MagickFormat.Ico);
                break;
            case ImageFormat.Tga:
                image.Settings.Compression = o.TgaRle ? CompressionMethod.RLE : CompressionMethod.NoCompression;
                image.Write(output, MagickFormat.Tga);
                break;
            case ImageFormat.Dds:
                image.Settings.SetDefine(MagickFormat.Dds, "compression", o.DdsCompression switch
                {
                    DdsCompression.Bc1 => "dxt1",
                    DdsCompression.None => "none",
                    _ => "dxt5"
                });
                image.Settings.SetDefine(MagickFormat.Dds, "cluster-fit", "true");
                if (!o.DdsMipmaps)
                {
                    image.Settings.SetDefine(MagickFormat.Dds, "mipmaps", "0");
                }

                image.Write(output, MagickFormat.Dds);
                break;
            default:
                image.Write(output);
                break;
        }
    }

    private static void ConvertAnimation(MagickImageCollection frames, string output, ImageOptions o, ImageFormat target, CancellationToken cancellationToken)
    {
        frames.Coalesce();
        foreach (var frame in frames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (frame is MagickImage image)
            {
                Resize(image, o, keepAlpha: true);
                if (o.StripMetadata)
                {
                    image.Strip();
                }

                if (target == ImageFormat.WebP)
                {
                    image.Quality = (uint)Math.Clamp(o.Quality, 1, 100);
                    image.Settings.SetDefine(MagickFormat.WebP, "lossless", o.WebpLossless ? "true" : "false");
                }
            }
        }

        if (target == ImageFormat.Gif)
        {
            frames.Quantize(new QuantizeSettings { Colors = 256, DitherMethod = o.GifDither ? DitherMethod.FloydSteinberg : DitherMethod.No });
            frames.Optimize();
            frames.Write(output, MagickFormat.Gif);
        }
        else
        {
            frames.Write(output, MagickFormat.WebP);
        }
    }

    // ================================================================ Icons

    /// <summary>Writes a multi-size ICO; with the favicon option also the web package into <paramref name="packageDirectory"/>.</summary>
    public static List<string> CreateIcon(string input, string output, string? packageDirectory, IconOptions o, CancellationToken cancellationToken)
    {
        var sizes = o.SelectedSizes();
        if (sizes.Length == 0)
        {
            sizes = [256, 48, 32, 16];
        }

        using var square = LoadSquare(input, o);
        using (var collection = new MagickImageCollection())
        {
            foreach (var size in sizes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                collection.Add(RenderSize(square, size, o));
            }

            collection.Write(output, MagickFormat.Ico);
        }

        var written = new List<string> { output };
        if (o.FaviconPackage && !string.IsNullOrEmpty(packageDirectory))
        {
            written.AddRange(WriteFaviconPackage(square, packageDirectory, o, cancellationToken));
        }

        return written;
    }

    /// <summary>One icon size as PNG bytes (preview on light and dark tiles).</summary>
    public static byte[] IconPreview(string input, int size, IconOptions o)
    {
        using var square = LoadSquare(input, o);
        using var image = RenderSize(square, size, o);
        return image.ToByteArray(MagickFormat.Png);
    }

    private static MagickImage LoadSquare(string input, IconOptions o)
    {
        var image = new MagickImage();
        image.Read(input, new MagickReadSettings { FrameIndex = 0, FrameCount = 1, BackgroundColor = MagickColors.Transparent });
        image.AutoOrient();
        image.BackgroundColor = MagickColors.Transparent;
        if (image.Width != image.Height)
        {
            if (o.Fit == IconFit.Crop)
            {
                var side = Math.Min(image.Width, image.Height);
                image.Crop(side, side, Gravity.Center);
                image.ResetPage();
            }
            else
            {
                var side = Math.Max(image.Width, image.Height);
                image.Extent(side, side, Gravity.Center);
            }
        }

        return image;
    }

    private static MagickImage RenderSize(MagickImage square, int size, IconOptions o)
    {
        var frame = (MagickImage)square.Clone();
        frame.FilterType = FilterType.Lanczos;
        frame.BackgroundColor = MagickColors.Transparent;
        var inner = (uint)Math.Max(1, Math.Round(size * (1 - 2 * Math.Clamp(o.PaddingPercent, 0, 40) / 100d)));
        frame.Resize(new MagickGeometry(inner, inner) { IgnoreAspectRatio = true });
        if (o.SharpenSmall && size <= 48)
        {
            frame.Sharpen(0, 0.5);
        }

        frame.Extent((uint)size, (uint)size, Gravity.Center);
        frame.Strip();
        return frame;
    }

    private static IEnumerable<string> WriteFaviconPackage(MagickImage square, string dir, IconOptions o, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(dir);
        var files = new List<string>();

        var favicon = Path.Combine(dir, "favicon.ico");
        using (var collection = new MagickImageCollection())
        {
            foreach (var size in new[] { 48, 32, 16 })
            {
                collection.Add(RenderSize(square, size, o));
            }

            collection.Write(favicon, MagickFormat.Ico);
        }

        files.Add(favicon);
        foreach (var (name, size) in new[] { ("apple-touch-icon.png", 180), ("android-chrome-192x192.png", 192), ("android-chrome-512x512.png", 512), ("favicon-32x32.png", 32), ("favicon-16x16.png", 16) })
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var png = RenderSize(square, size, o);
            var path = Path.Combine(dir, name);
            png.Write(path, MagickFormat.Png);
            files.Add(path);
        }

        var manifest = new
        {
            name = "",
            short_name = "",
            icons = new[]
            {
                new { src = "/android-chrome-192x192.png", sizes = "192x192", type = "image/png" },
                new { src = "/android-chrome-512x512.png", sizes = "512x512", type = "image/png" }
            },
            theme_color = "#ffffff",
            background_color = "#ffffff",
            display = "standalone"
        };
        var manifestPath = Path.Combine(dir, "site.webmanifest");
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        files.Add(manifestPath);

        var html = Path.Combine(dir, "head-snippet.html");
        File.WriteAllText(html,
            """
            <link rel="icon" href="/favicon.ico" sizes="any">
            <link rel="icon" type="image/png" sizes="32x32" href="/favicon-32x32.png">
            <link rel="icon" type="image/png" sizes="16x16" href="/favicon-16x16.png">
            <link rel="apple-touch-icon" sizes="180x180" href="/apple-touch-icon.png">
            <link rel="manifest" href="/site.webmanifest">
            """);
        files.Add(html);
        return files;
    }

    private static MagickColor Color(string? hex, MagickColor fallback)
    {
        try
        {
            return string.IsNullOrWhiteSpace(hex) ? fallback : new MagickColor(hex.Trim());
        }
        catch
        {
            return fallback;
        }
    }
}
