using System.Text;
using CONVERTit.Core.GameDev;
using ImageMagick;

namespace CONVERTit.Engines.Image;

/// <summary>Settings of the app icon generator (mini editor: scale and position of the motif, background).</summary>
public sealed class AppIconSettings
{
    public AppIconTarget Targets { get; set; } = AppIconTarget.Android | AppIconTarget.Ios | AppIconTarget.Web;

    /// <summary>Motif size in percent of the usable area (safe zone).</summary>
    public int Scale { get; set; } = 100;

    /// <summary>Shift of the motif in percent of the usable area (−50 … 50).</summary>
    public int OffsetX { get; set; }

    public int OffsetY { get; set; }

    /// <summary>Background color (#RRGGBB); used for opaque icons and adaptive backgrounds.</summary>
    public string Background { get; set; } = "#FFFFFF";

    /// <summary>Keep the background transparent where the platform allows it (Windows, Web, Android legacy).</summary>
    public bool TransparentBackground { get; set; }

    /// <summary>App name for site.webmanifest.</summary>
    public string AppName { get; set; } = "App";
}

/// <summary>Preview masks of the editor.</summary>
public enum IconPreviewShape { Square, Circle, Squircle, Rounded, Banner }

/// <summary>Sprite sheet settings.</summary>
public sealed class SpriteSettings
{
    public SpriteLayout Layout { get; set; } = SpriteLayout.Grid;

    /// <summary>Grid columns, 0 = automatic (about square).</summary>
    public int Columns { get; set; }

    /// <summary>Pixels between frames (against color bleeding when the engine filters).</summary>
    public int Spacing { get; set; } = 2;

    public bool PowerOfTwo { get; set; }

    /// <summary>Cut transparent borders; all frames of an animation get the same crop (no jumping).</summary>
    public bool TrimTransparent { get; set; } = true;

    /// <summary>Scale factor of every frame (0.1 – 8).</summary>
    public double Scale { get; set; } = 1;

    /// <summary>Pixel art: scale without smoothing, palette without dithering.</summary>
    public bool PixelArt { get; set; }

    /// <summary>Reduce colors (0 = off, e.g. 16, 32, 64).</summary>
    public int PaletteColors { get; set; }

    /// <summary>Outline around the motif in pixels (0 = off).</summary>
    public int Outline { get; set; }

    public string OutlineColor { get; set; } = "#000000";

    public SpriteMetadata Metadata { get; set; } = SpriteMetadata.Json | SpriteMetadata.GodotTres;

    /// <summary>Frames per second of the animations (.tres speed, JSON frameRate).</summary>
    public int Fps { get; set; } = 12;

    public bool Loop { get; set; } = true;

    /// <summary>Folder of the sheet inside the Godot project (res://…).</summary>
    public string GodotFolder { get; set; } = "res://sprites/";
}

/// <summary>A source of sprite frames: one image, or the frames of a GIF/WebP/video (already extracted).</summary>
public sealed record SpriteSource(string Animation, IReadOnlyList<string> Files);

/// <summary>
/// Game development tools on top of ImageMagick: app icon sets (Android, iOS, macOS, Windows, Web, Godot), sprite
/// sheets with Godot/TexturePacker metadata and normal maps for 2D lighting.
/// </summary>
public static class GameDevEngine
{
    // ================================================================ App icons

    /// <summary>Writes all icon files of the selected platforms into <paramref name="outputDirectory"/>. Returns the files.</summary>
    public static List<string> WriteAppIcons(string input, string outputDirectory, AppIconSettings o, CancellationToken cancellationToken)
    {
        using var motif = LoadMotif(input);
        var written = new List<string>();
        foreach (var spec in AppIconSpecs.For(o.Targets))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = Path.Combine(outputDirectory, spec.Path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            switch (spec.Container)
            {
                case IconContainer.Ico:
                {
                    using var collection = new MagickImageCollection();
                    foreach (var size in spec.Sizes ?? [spec.Width])
                    {
                        collection.Add(Render(motif, spec with { Width = size, Height = size }, o));
                    }

                    collection.Write(path, MagickFormat.Ico);
                    break;
                }
                case IconContainer.Icns:
                {
                    var entries = new List<(int, byte[])>();
                    foreach (var size in spec.Sizes ?? [spec.Width])
                    {
                        using var image = Render(motif, spec with { Width = size, Height = size }, o);
                        entries.Add((size, image.ToByteArray(MagickFormat.Png)));
                    }

                    File.WriteAllBytes(path, IcnsWriter.Build(entries));
                    break;
                }
                default:
                {
                    using var image = Render(motif, spec, o);
                    image.Write(path, MagickFormat.Png32);
                    break;
                }
            }

            written.Add(path);
        }

        // Extra files the platforms expect next to the images.
        void Text(string relative, string content)
        {
            var path = Path.Combine(outputDirectory, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content, new UTF8Encoding(false));
            written.Add(path);
        }

        if (o.Targets.HasFlag(AppIconTarget.Android) || o.Targets.HasFlag(AppIconTarget.AndroidTv))
        {
            Text("android/res/mipmap-anydpi-v26/ic_launcher.xml", AppIconSpecs.AndroidAdaptiveXml());
            Text("android/res/mipmap-anydpi-v26/ic_launcher_round.xml", AppIconSpecs.AndroidAdaptiveXml());
        }

        if (o.Targets.HasFlag(AppIconTarget.Ios))
        {
            Text("ios/AppIcon.appiconset/Contents.json", AppIconSpecs.IosContentsJson());
        }

        if (o.Targets.HasFlag(AppIconTarget.Web))
        {
            Text("web/site.webmanifest", AppIconSpecs.WebManifest(o.AppName, o.Background));
        }

        if (o.Targets.HasFlag(AppIconTarget.Godot))
        {
            Text("godot/README.txt", AppIconSpecs.GodotReadme());
        }

        return written;
    }

    /// <summary>Editor preview (PNG) of one layer with a mask shape, e.g. Android adaptive in a circle.</summary>
    public static byte[] PreviewAppIcon(string input, AppIconSettings o, IconLayer layer, IconPreviewShape shape, int size, bool safeZone)
    {
        using var motif = LoadMotif(input);
        var height = shape == IconPreviewShape.Banner ? size * 9 / 16 : size;
        var spec = new IconFileSpec("preview.png", size, height, shape == IconPreviewShape.Banner ? IconLayer.Banner : layer);
        MagickImage image;
        if (layer == IconLayer.AdaptiveForeground)
        {
            // Adaptive icon as the launcher shows it: background + foreground, masked.
            image = Render(motif, spec with { Layer = IconLayer.AdaptiveBackground }, o);
            using var front = Render(motif, spec, o);
            image.Composite(front, CompositeOperator.Over);
        }
        else
        {
            image = Render(motif, spec, o);
        }

        using (image)
        {
            ApplyMask(image, shape);
            if (safeZone)
            {
                DrawSafeZone(image, size * AppIconSpecs.SafeFactor(layer) / 2);
            }

            return image.ToByteArray(MagickFormat.Png);
        }
    }

    private static MagickImage LoadMotif(string input)
    {
        var motif = new MagickImage();
        motif.Read(input, new MagickReadSettings { FrameIndex = 0, FrameCount = 1, BackgroundColor = MagickColors.Transparent });
        motif.AutoOrient();
        if (!motif.HasAlpha)
        {
            motif.Alpha(AlphaOption.Set);
        }

        return motif;
    }

    /// <summary>One icon image for a spec: background, motif scaled into the safe zone and shifted, masks, opacity.</summary>
    private static MagickImage Render(MagickImage motif, IconFileSpec spec, AppIconSettings o)
    {
        var w = (uint)spec.Width;
        var h = (uint)spec.Height;
        var background = Color(o.Background);
        var fillBackground = spec.Layer is IconLayer.AdaptiveBackground or IconLayer.Maskable or IconLayer.Banner
            || spec.Opaque || !o.TransparentBackground && spec.Layer is IconLayer.Full or IconLayer.Round;
        var canvas = new MagickImage(fillBackground ? background : MagickColors.Transparent, w, h);
        if (spec.Layer == IconLayer.AdaptiveBackground)
        {
            return canvas;
        }

        var box = Math.Min(w, h) * AppIconSpecs.SafeFactor(spec.Layer) * Math.Clamp(o.Scale, 10, 200) / 100.0;
        using var scaled = (MagickImage)motif.Clone();
        scaled.FilterType = FilterType.Lanczos;
        scaled.Resize(new MagickGeometry((uint)Math.Max(1, Math.Round(box)), (uint)Math.Max(1, Math.Round(box))));
        if (spec.Layer == IconLayer.AdaptiveMonochrome)
        {
            // Silhouette: white with the motif's alpha.
            using var alpha = (MagickImage)scaled.Clone();
            alpha.Alpha(AlphaOption.Extract);
            scaled.Colorize(MagickColors.White, new Percentage(100));
            scaled.Composite(alpha, CompositeOperator.CopyAlpha);
        }

        var x = (int)Math.Round((w - scaled.Width) / 2.0 + box * Math.Clamp(o.OffsetX, -50, 50) / 100.0);
        var y = (int)Math.Round((h - scaled.Height) / 2.0 + box * Math.Clamp(o.OffsetY, -50, 50) / 100.0);
        canvas.Composite(scaled, x, y, CompositeOperator.Over);

        if (spec.Layer == IconLayer.Round)
        {
            ApplyMask(canvas, IconPreviewShape.Circle);
        }

        if (spec.Opaque)
        {
            canvas.BackgroundColor = background;
            canvas.Alpha(AlphaOption.Remove);
            canvas.Alpha(AlphaOption.Off);
        }

        return canvas;
    }

    /// <summary>Cuts the image to a circle or rounded square (alpha multiplied pixel by pixel, smooth edge).</summary>
    private static void ApplyMask(MagickImage image, IconPreviewShape shape)
    {
        if (shape is IconPreviewShape.Square or IconPreviewShape.Banner)
        {
            return;
        }

        if (!image.HasAlpha)
        {
            image.Alpha(AlphaOption.Set);
        }

        var w = (int)image.Width;
        var h = (int)image.Height;
        using var pixels = image.GetPixels();
        var rgba = pixels.ToByteArray(PixelMapping.RGBA) ?? throw new InvalidOperationException("No pixel data");
        var radius = shape switch
        {
            IconPreviewShape.Circle => Math.Min(w, h) / 2.0,
            IconPreviewShape.Squircle => Math.Min(w, h) * 0.3,
            _ => Math.Min(w, h) * 0.18
        };
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                // Distance outside the rounded rectangle (circle = rounded rectangle with radius w/2).
                var px = x + 0.5;
                var py = y + 0.5;
                var cx = Math.Clamp(px, radius, w - radius);
                var cy = Math.Clamp(py, radius, h - radius);
                var d = Math.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy)) - radius;
                var coverage = Math.Clamp(0.5 - d, 0, 1);
                var i = (y * w + x) * 4 + 3;
                rgba[i] = (byte)Math.Round(rgba[i] * coverage);
            }
        }

        image.ReadPixels(rgba, new PixelReadSettings((uint)w, (uint)h, StorageType.Char, PixelMapping.RGBA));
    }

    /// <summary>Dashed accent circle that shows the safe zone in the editor preview.</summary>
    private static void DrawSafeZone(MagickImage image, double radius)
    {
        var w = (int)image.Width;
        var h = (int)image.Height;
        using var pixels = image.GetPixels();
        var rgba = pixels.ToByteArray(PixelMapping.RGBA) ?? throw new InvalidOperationException("No pixel data");
        var thickness = Math.Max(1.0, w / 160.0);
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var dx = x + 0.5 - w / 2.0;
                var dy = y + 0.5 - h / 2.0;
                var d = Math.Abs(Math.Sqrt(dx * dx + dy * dy) - radius);
                var angle = Math.Atan2(dy, dx);
                // 36 dashes around the circle.
                if (d <= thickness && (int)Math.Floor((angle + Math.PI) / (Math.PI / 18)) % 2 == 0)
                {
                    var i = (y * w + x) * 4;
                    rgba[i] = 0xBB;
                    rgba[i + 1] = 0xF2;
                    rgba[i + 2] = 0x36;
                    rgba[i + 3] = 255;
                }
            }
        }

        image.ReadPixels(rgba, new PixelReadSettings((uint)w, (uint)h, StorageType.Char, PixelMapping.RGBA));
    }

    private static MagickColor Color(string? hex)
    {
        try
        {
            return new MagickColor(string.IsNullOrWhiteSpace(hex) ? "#FFFFFF" : hex.Trim());
        }
        catch (MagickException)
        {
            return MagickColors.White;
        }
    }

    // ================================================================ Icon export formats (Image › Icon & Favicon)

    /// <summary>.icns from a square master (16 … 1024 px, PNG entries).</summary>
    public static void WriteIcns(string input, string output, IReadOnlyList<int> sizes)
    {
        using var motif = LoadMotif(input);
        var settings = new AppIconSettings { TransparentBackground = true, Background = "#00000000" };
        var entries = new List<(int, byte[])>();
        foreach (var size in sizes.Where(s => s is 16 or 32 or 64 or 128 or 256 or 512 or 1024).Distinct().OrderBy(s => s))
        {
            using var image = Render(motif, new IconFileSpec("x", size, size, IconLayer.Full), settings);
            entries.Add((size, image.ToByteArray(MagickFormat.Png)));
        }

        File.WriteAllBytes(output, IcnsWriter.Build(entries));
    }

    /// <summary>PNG set: the largest size as <paramref name="output"/>, all others as "name_16.png" … next to it.</summary>
    public static List<string> WritePngSet(string input, string output, IReadOnlyList<int> sizes, string? finalPath)
    {
        using var motif = LoadMotif(input);
        var settings = new AppIconSettings { TransparentBackground = true };
        var written = new List<string>();
        var ordered = sizes.Distinct().OrderByDescending(s => s).ToList();
        var stem = Path.GetFileNameWithoutExtension(finalPath ?? output);
        var dir = Path.GetDirectoryName(finalPath ?? output) ?? "";
        for (var i = 0; i < ordered.Count; i++)
        {
            using var image = Render(motif, new IconFileSpec("x", ordered[i], ordered[i], IconLayer.Full), settings);
            var path = i == 0 ? output : Path.Combine(dir, $"{stem}_{ordered[i]}.png");
            image.Write(path, MagickFormat.Png32);
            written.Add(path);
        }

        return written;
    }

    // ================================================================ Sprite sheets

    /// <summary>Builds the sheet (PNG) and the metadata files next to it. Returns the written files.</summary>
    public static List<string> WriteSpriteSheet(IReadOnlyList<SpriteSource> sources, string output, string finalPath, SpriteSettings o,
        CancellationToken cancellationToken)
    {
        var frames = new List<(SpriteFrame Frame, MagickImage Image)>();
        try
        {
            foreach (var source in sources)
            {
                var images = new List<MagickImage>();
                foreach (var file in source.Files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using var collection = new MagickImageCollection(file);
                    collection.Coalesce();
                    images.AddRange(collection.Select(f => (MagickImage)f.Clone()));
                }

                PrepareFrames(images, o);
                var name = Path.GetFileNameWithoutExtension(finalPath);
                for (var i = 0; i < images.Count; i++)
                {
                    frames.Add((new SpriteFrame($"{source.Animation}_{i:000}", source.Animation, (int)images[i].Width, (int)images[i].Height), images[i]));
                }
            }

            var atlas = SpriteSheet.Layout(frames.Select(f => f.Frame).ToList(), o.Layout, o.Columns, o.Spacing, 0, o.PowerOfTwo);
            using var sheet = new MagickImage(MagickColors.Transparent, (uint)Math.Max(1, atlas.Width), (uint)Math.Max(1, atlas.Height));
            var byFrame = frames.ToDictionary(f => f.Frame, f => f.Image);
            foreach (var placed in atlas.Frames)
            {
                sheet.Composite(byFrame[placed.Frame], placed.X, placed.Y, CompositeOperator.Over);
            }

            sheet.Write(output, MagickFormat.Png32);
            var written = new List<string> { finalPath };
            var stem = Path.GetFileNameWithoutExtension(finalPath);
            var dir = Path.GetDirectoryName(finalPath) ?? "";
            var imageName = Path.GetFileName(finalPath);
            if (o.Metadata.HasFlag(SpriteMetadata.Json))
            {
                var json = Path.Combine(dir, stem + ".json");
                File.WriteAllText(json, SpriteSheet.TexturePackerJson(atlas, imageName, o.Fps), new UTF8Encoding(false));
                written.Add(json);
            }

            if (o.Metadata.HasFlag(SpriteMetadata.GodotTres))
            {
                var folder = string.IsNullOrWhiteSpace(o.GodotFolder) ? "res://" : o.GodotFolder.TrimEnd('/') + "/";
                var tres = Path.Combine(dir, stem + "_frames.tres");
                File.WriteAllText(tres, SpriteSheet.GodotSpriteFrames(atlas, folder + imageName, o.Fps, o.Loop), new UTF8Encoding(false));
                written.Add(tres);
            }

            return written;
        }
        finally
        {
            foreach (var (_, image) in frames)
            {
                image.Dispose();
            }
        }
    }

    /// <summary>Uniform trim, scale, palette and outline for the frames of one animation.</summary>
    private static void PrepareFrames(List<MagickImage> images, SpriteSettings o)
    {
        if (images.Count == 0)
        {
            return;
        }

        if (o.TrimTransparent)
        {
            // Union of the visible areas, so the animation does not jump.
            int left = int.MaxValue, top = int.MaxValue, right = 0, bottom = 0;
            foreach (var image in images)
            {
                if (image.BoundingBox is { } box && box.Width > 0 && box.Height > 0)
                {
                    left = Math.Min(left, box.X);
                    top = Math.Min(top, box.Y);
                    right = Math.Max(right, box.X + (int)box.Width);
                    bottom = Math.Max(bottom, box.Y + (int)box.Height);
                }
            }

            if (right > left && bottom > top)
            {
                foreach (var image in images)
                {
                    image.Crop(new MagickGeometry(left, top, (uint)(right - left), (uint)(bottom - top)));
                    image.ResetPage();
                }
            }
        }

        foreach (var image in images)
        {
            if (Math.Abs(o.Scale - 1) > 0.001)
            {
                var scale = Math.Clamp(o.Scale, 0.1, 8);
                image.FilterType = o.PixelArt ? FilterType.Point : FilterType.Lanczos;
                image.Resize(new MagickGeometry((uint)Math.Max(1, Math.Round(image.Width * scale)), (uint)Math.Max(1, Math.Round(image.Height * scale)))
                {
                    IgnoreAspectRatio = true
                });
            }

            if (o.PaletteColors is >= 2 and <= 256)
            {
                image.Quantize(new QuantizeSettings
                {
                    Colors = (uint)o.PaletteColors,
                    DitherMethod = o.PixelArt ? DitherMethod.No : DitherMethod.FloydSteinberg
                });
            }

            if (o.Outline > 0)
            {
                AddOutline(image, o.Outline, Color(o.OutlineColor));
            }
        }
    }

    /// <summary>Outline: the alpha area grown by <paramref name="width"/> px, filled with the color, behind the motif.</summary>
    private static void AddOutline(MagickImage image, int width, MagickColor color)
    {
        // Room for the outline at the edges.
        image.BorderColor = MagickColors.Transparent;
        image.Border((uint)width);
        using var outline = (MagickImage)image.Clone();
        outline.Alpha(AlphaOption.Extract);
        outline.Morphology(new MorphologySettings { Method = MorphologyMethod.Dilate, Kernel = Kernel.Disk, KernelArguments = width.ToString() });
        using var fill = new MagickImage(color, image.Width, image.Height);
        fill.Composite(outline, CompositeOperator.CopyAlpha);
        fill.Composite(image, CompositeOperator.Over);
        image.CopyPixels(fill);
    }

    // ================================================================ Normal maps

    /// <summary>
    /// Normal map from the brightness of an image (bright = high), Sobel filter. Green up = OpenGL convention
    /// (Godot, Unity, Blender); <paramref name="invertY"/> for DirectX/Unreal. Transparent pixels stay flat.
    /// </summary>
    public static void NormalMap(string input, string output, double strength, bool invertY)
    {
        using var image = new MagickImage();
        image.Read(input, new MagickReadSettings { FrameIndex = 0, FrameCount = 1 });
        var w = (int)image.Width;
        var h = (int)image.Height;
        using var pixels = image.GetPixels();
        var rgba = pixels.ToByteArray(PixelMapping.RGBA) ?? throw new InvalidOperationException("No pixel data");
        var height = new double[w * h];
        for (var i = 0; i < w * h; i++)
        {
            var a = rgba[i * 4 + 3] / 255.0;
            height[i] = (0.299 * rgba[i * 4] + 0.587 * rgba[i * 4 + 1] + 0.114 * rgba[i * 4 + 2]) / 255.0 * a;
        }

        double H(int x, int y) => height[Math.Clamp(y, 0, h - 1) * w + Math.Clamp(x, 0, w - 1)];
        var result = new byte[w * h * 4];
        var k = Math.Clamp(strength, 0.1, 20);
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var dx = (H(x + 1, y - 1) + 2 * H(x + 1, y) + H(x + 1, y + 1)) - (H(x - 1, y - 1) + 2 * H(x - 1, y) + H(x - 1, y + 1));
                var dy = (H(x - 1, y + 1) + 2 * H(x, y + 1) + H(x + 1, y + 1)) - (H(x - 1, y - 1) + 2 * H(x, y - 1) + H(x + 1, y - 1));
                var nx = -dx * k;
                var ny = (invertY ? -1 : 1) * dy * k;
                const double nz = 1.0;
                var len = Math.Sqrt(nx * nx + ny * ny + nz * nz);
                var i = (y * w + x) * 4;
                result[i] = (byte)Math.Round((nx / len * 0.5 + 0.5) * 255);
                result[i + 1] = (byte)Math.Round((ny / len * 0.5 + 0.5) * 255);
                result[i + 2] = (byte)Math.Round((nz / len * 0.5 + 0.5) * 255);
                result[i + 3] = 255;
            }
        }

        using var normal = new MagickImage();
        normal.ReadPixels(result, new PixelReadSettings((uint)w, (uint)h, StorageType.Char, PixelMapping.RGBA));
        normal.Alpha(AlphaOption.Off);
        normal.Write(output, MagickFormat.Png24);
    }
}
