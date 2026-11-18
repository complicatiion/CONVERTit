using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CONVERTit.Core.GameDev;

/// <summary>Platforms of the app icon generator.</summary>
[Flags]
public enum AppIconTarget
{
    None = 0,
    Android = 1,
    AndroidTv = 2,
    Ios = 4,
    MacOs = 8,
    Windows = 16,
    Web = 32,
    Godot = 64
}

/// <summary>How the motif is placed in an icon file.</summary>
public enum IconLayer
{
    /// <summary>Background and motif together (most icons).</summary>
    Full,

    /// <summary>Android adaptive icon foreground: motif only, inside the 72 dp safe zone of 108 dp, transparent.</summary>
    AdaptiveForeground,

    /// <summary>Android adaptive icon background: background color/fill only.</summary>
    AdaptiveBackground,

    /// <summary>Android 13+ / Godot monochrome layer: white silhouette of the motif in the safe zone.</summary>
    AdaptiveMonochrome,

    /// <summary>Round launcher icon (circle mask).</summary>
    Round,

    /// <summary>PWA "maskable" icon: motif inside the 80 % safe circle, opaque.</summary>
    Maskable,

    /// <summary>Wide banner (Android TV 320 × 180): motif centered on the background.</summary>
    Banner
}

/// <summary>Container a set of sizes is written into.</summary>
public enum IconContainer { Png, Ico, Icns }

/// <summary>One file of an icon set.</summary>
/// <param name="Path">Relative path inside the output folder ("android/mipmap-xxxhdpi/ic_launcher.png").</param>
/// <param name="Sizes">One size for PNG; several for ICO/ICNS.</param>
/// <param name="Opaque">No transparency allowed (iOS, apple-touch-icon): flattened on the background.</param>
public sealed record IconFileSpec(string Path, int Width, int Height, IconLayer Layer, IconContainer Container = IconContainer.Png,
    IReadOnlyList<int>? Sizes = null, bool Opaque = false);

/// <summary>
/// Exact file names and sizes for app icons (pure data, unit-tested): Android (legacy, round, adaptive with
/// monochrome layer, Play Store), Android TV banner, iOS/iPadOS asset catalog, macOS .icns, Windows .ico, Web/PWA
/// and the icons the Godot export presets ask for.
/// </summary>
public static class AppIconSpecs
{
    private static readonly (string Density, double Scale)[] AndroidDensities =
        [("mdpi", 1), ("hdpi", 1.5), ("xhdpi", 2), ("xxhdpi", 3), ("xxxhdpi", 4)];

    /// <summary>iOS/iPadOS: (point size, scale, idiom) of the classic AppIcon.appiconset plus the 1024 marketing icon.</summary>
    public static readonly (double Points, int Scale, string Idiom)[] IosIcons =
    [
        (20, 2, "iphone"), (20, 3, "iphone"), (29, 2, "iphone"), (29, 3, "iphone"), (40, 2, "iphone"), (40, 3, "iphone"),
        (60, 2, "iphone"), (60, 3, "iphone"),
        (20, 1, "ipad"), (20, 2, "ipad"), (29, 1, "ipad"), (29, 2, "ipad"), (40, 1, "ipad"), (40, 2, "ipad"), (76, 1, "ipad"),
        (76, 2, "ipad"), (83.5, 2, "ipad"),
        (1024, 1, "ios-marketing")
    ];

    public static IReadOnlyList<IconFileSpec> For(AppIconTarget targets)
    {
        var files = new List<IconFileSpec>();
        if (targets.HasFlag(AppIconTarget.Android) || targets.HasFlag(AppIconTarget.AndroidTv))
        {
            foreach (var (density, scale) in AndroidDensities)
            {
                var launcher = (int)Math.Round(48 * scale);
                var adaptive = (int)Math.Round(108 * scale);
                var dir = $"android/res/mipmap-{density}";
                files.Add(new IconFileSpec($"{dir}/ic_launcher.png", launcher, launcher, IconLayer.Full));
                files.Add(new IconFileSpec($"{dir}/ic_launcher_round.png", launcher, launcher, IconLayer.Round));
                files.Add(new IconFileSpec($"{dir}/ic_launcher_foreground.png", adaptive, adaptive, IconLayer.AdaptiveForeground));
                files.Add(new IconFileSpec($"{dir}/ic_launcher_background.png", adaptive, adaptive, IconLayer.AdaptiveBackground));
                files.Add(new IconFileSpec($"{dir}/ic_launcher_monochrome.png", adaptive, adaptive, IconLayer.AdaptiveMonochrome));
            }

            files.Add(new IconFileSpec("android/playstore-icon.png", 512, 512, IconLayer.Full));
        }

        if (targets.HasFlag(AppIconTarget.AndroidTv))
        {
            files.Add(new IconFileSpec("android/res/drawable-xhdpi/banner.png", 320, 180, IconLayer.Banner, Opaque: true));
        }

        if (targets.HasFlag(AppIconTarget.Ios))
        {
            foreach (var (points, scale, idiom) in IosIcons)
            {
                var px = (int)Math.Round(points * scale);
                files.Add(new IconFileSpec($"ios/AppIcon.appiconset/{IosFileName(points, scale, idiom)}", px, px, IconLayer.Full, Opaque: true));
            }
        }

        if (targets.HasFlag(AppIconTarget.MacOs))
        {
            files.Add(new IconFileSpec("macos/AppIcon.icns", 1024, 1024, IconLayer.Full, IconContainer.Icns, [16, 32, 64, 128, 256, 512, 1024]));
        }

        if (targets.HasFlag(AppIconTarget.Windows))
        {
            files.Add(new IconFileSpec("windows/app.ico", 256, 256, IconLayer.Full, IconContainer.Ico, [16, 24, 32, 48, 64, 128, 256]));
        }

        if (targets.HasFlag(AppIconTarget.Web))
        {
            files.Add(new IconFileSpec("web/favicon.ico", 48, 48, IconLayer.Full, IconContainer.Ico, [16, 32, 48]));
            files.Add(new IconFileSpec("web/favicon-16x16.png", 16, 16, IconLayer.Full));
            files.Add(new IconFileSpec("web/favicon-32x32.png", 32, 32, IconLayer.Full));
            files.Add(new IconFileSpec("web/apple-touch-icon.png", 180, 180, IconLayer.Full, Opaque: true));
            files.Add(new IconFileSpec("web/icon-192.png", 192, 192, IconLayer.Full));
            files.Add(new IconFileSpec("web/icon-512.png", 512, 512, IconLayer.Full));
            files.Add(new IconFileSpec("web/icon-maskable-512.png", 512, 512, IconLayer.Maskable, Opaque: true));
        }

        if (targets.HasFlag(AppIconTarget.Godot))
        {
            // Names follow the fields of Godot's export presets (see godot/README.txt that is written along).
            files.Add(new IconFileSpec("godot/icon.png", 256, 256, IconLayer.Full));
            files.Add(new IconFileSpec("godot/windows/icon.ico", 256, 256, IconLayer.Full, IconContainer.Ico, [16, 32, 48, 64, 128, 256]));
            files.Add(new IconFileSpec("godot/android/main_192x192.png", 192, 192, IconLayer.Full));
            files.Add(new IconFileSpec("godot/android/adaptive_foreground_432x432.png", 432, 432, IconLayer.AdaptiveForeground));
            files.Add(new IconFileSpec("godot/android/adaptive_background_432x432.png", 432, 432, IconLayer.AdaptiveBackground));
            files.Add(new IconFileSpec("godot/android/adaptive_monochrome_432x432.png", 432, 432, IconLayer.AdaptiveMonochrome));
            files.Add(new IconFileSpec("godot/ios/app_store_1024x1024.png", 1024, 1024, IconLayer.Full, Opaque: true));
            files.Add(new IconFileSpec("godot/macos/icon.icns", 1024, 1024, IconLayer.Full, IconContainer.Icns, [16, 32, 64, 128, 256, 512, 1024]));
            files.Add(new IconFileSpec("godot/web/icon_144x144.png", 144, 144, IconLayer.Full));
            files.Add(new IconFileSpec("godot/web/icon_180x180.png", 180, 180, IconLayer.Full, Opaque: true));
            files.Add(new IconFileSpec("godot/web/icon_512x512.png", 512, 512, IconLayer.Full));
        }

        return files;
    }

    public static string IosFileName(double points, int scale, string idiom)
    {
        var size = points.ToString("0.#", CultureInfo.InvariantCulture);
        return idiom == "ios-marketing" ? "icon-1024.png" : $"icon-{idiom}-{size}@{scale}x.png";
    }

    /// <summary>Share of the icon edge the motif may fill for a layer (safe zones).</summary>
    public static double SafeFactor(IconLayer layer) => layer switch
    {
        IconLayer.AdaptiveForeground or IconLayer.AdaptiveMonochrome => 72.0 / 108.0,
        IconLayer.Maskable => 0.8,
        IconLayer.Round => 0.86,
        IconLayer.Banner => 0.8,
        _ => 1.0
    };

    /// <summary>Contents.json of the iOS asset catalog (Xcode).</summary>
    public static string IosContentsJson()
    {
        var images = new JsonArray();
        foreach (var (points, scale, idiom) in IosIcons)
        {
            var size = points.ToString("0.#", CultureInfo.InvariantCulture);
            images.Add(new JsonObject
            {
                ["filename"] = IosFileName(points, scale, idiom),
                ["idiom"] = idiom,
                ["scale"] = $"{scale}x",
                ["size"] = $"{size}x{size}"
            });
        }

        var root = new JsonObject { ["images"] = images, ["info"] = new JsonObject { ["author"] = "CONVERTit", ["version"] = 1 } };
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>Adaptive icon XML (res/mipmap-anydpi-v26/ic_launcher.xml and ic_launcher_round.xml).</summary>
    public static string AndroidAdaptiveXml() =>
        """
        <?xml version="1.0" encoding="utf-8"?>
        <adaptive-icon xmlns:android="http://schemas.android.com/apk/res/android">
            <background android:drawable="@mipmap/ic_launcher_background" />
            <foreground android:drawable="@mipmap/ic_launcher_foreground" />
            <monochrome android:drawable="@mipmap/ic_launcher_monochrome" />
        </adaptive-icon>
        """;

    /// <summary>site.webmanifest for the web icons.</summary>
    public static string WebManifest(string name, string themeColor) =>
        new JsonObject
        {
            ["name"] = name,
            ["short_name"] = name,
            ["icons"] = new JsonArray
            {
                new JsonObject { ["src"] = "icon-192.png", ["sizes"] = "192x192", ["type"] = "image/png" },
                new JsonObject { ["src"] = "icon-512.png", ["sizes"] = "512x512", ["type"] = "image/png" },
                new JsonObject { ["src"] = "icon-maskable-512.png", ["sizes"] = "512x512", ["type"] = "image/png", ["purpose"] = "maskable" }
            },
            ["theme_color"] = themeColor,
            ["background_color"] = themeColor,
            ["display"] = "standalone"
        }.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

    /// <summary>Where the Godot files go in the export presets.</summary>
    public static string GodotReadme() =>
        """
        CONVERTit – icons for the Godot export presets (Project › Export)

        icon.png                                   Project Settings › Application › Config › Icon
        windows/icon.ico                           Windows Desktop › Application › Icon
        android/main_192x192.png                   Android › Launcher Icons › Main 192 x 192
        android/adaptive_foreground_432x432.png    Android › Launcher Icons › Adaptive Foreground 432 x 432
        android/adaptive_background_432x432.png    Android › Launcher Icons › Adaptive Background 432 x 432
        android/adaptive_monochrome_432x432.png    Android › Launcher Icons › Adaptive Monochrome 432 x 432
        ios/app_store_1024x1024.png                iOS › Icons › App Store 1024 x 1024
        macos/icon.icns                            macOS › Application › Icon
        web/icon_144x144.png                       Web › Progressive Web App › Icon 144 x 144
        web/icon_180x180.png                       Web › Progressive Web App › Icon 180 x 180
        web/icon_512x512.png                       Web › Progressive Web App › Icon 512 x 512
        """;
}

/// <summary>
/// Apple .icns container: "icns" + total length, then one entry per size (type code + length + PNG data).
/// PNG entries are supported since macOS 10.7.
/// </summary>
public static class IcnsWriter
{
    public static string TypeFor(int size) => size switch
    {
        16 => "icp4",
        32 => "icp5",
        64 => "icp6",
        128 => "ic07",
        256 => "ic08",
        512 => "ic09",
        1024 => "ic10",
        _ => throw new ArgumentOutOfRangeException(nameof(size), size, "ICNS sizes: 16, 32, 64, 128, 256, 512, 1024")
    };

    public static byte[] Build(IReadOnlyList<(int Size, byte[] Png)> entries)
    {
        using var stream = new MemoryStream();
        var total = 8 + entries.Sum(e => 8 + e.Png.Length);
        stream.Write(Encoding.ASCII.GetBytes("icns"));
        WriteBigEndian(stream, total);
        foreach (var (size, png) in entries)
        {
            stream.Write(Encoding.ASCII.GetBytes(TypeFor(size)));
            WriteBigEndian(stream, 8 + png.Length);
            stream.Write(png);
        }

        return stream.ToArray();
    }

    private static void WriteBigEndian(Stream stream, int value)
    {
        stream.WriteByte((byte)(value >> 24));
        stream.WriteByte((byte)(value >> 16));
        stream.WriteByte((byte)(value >> 8));
        stream.WriteByte((byte)value);
    }
}
