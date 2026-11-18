using CommunityToolkit.Mvvm.ComponentModel;

namespace CONVERTit.Core.Options;

/// <summary>Image conversion settings (Image › Convert and Image › Resize).</summary>
public sealed partial class ImageOptions : ObservableObject
{
    [ObservableProperty] private ImageFormat _format = ImageFormat.Png;

    /// <summary>Quality 1–100 for lossy WebP and AVIF.</summary>
    [ObservableProperty] private int _quality = 85;

    /// <summary>JPEG quality 1–100: 95 by default (visually lossless; 100 is 2–3× larger without visible gain).</summary>
    [ObservableProperty] private int _jpegQuality = 95;

    // ---------------------------------------------------------------- PNG
    /// <summary>zlib level 0–9: lossless, only size and speed change.</summary>
    [ObservableProperty] private int _pngCompression = 9;

    /// <summary>Reduce to a palette (PNG-8): much smaller, lossy for photos.</summary>
    [ObservableProperty] private bool _pngPalette;

    [ObservableProperty] private int _paletteColors = 256;

    // ---------------------------------------------------------------- Transparency
    [ObservableProperty] private bool _keepTransparency = true;

    /// <summary>Fill for removed transparency and for formats without alpha (JPEG, BMP).</summary>
    [ObservableProperty] private string _backgroundColor = "#FFFFFF";

    // ---------------------------------------------------------------- Format specific
    [ObservableProperty] private bool _jpegProgressive = true;
    [ObservableProperty] private ChromaSubsampling _jpegChroma = ChromaSubsampling.S420;
    [ObservableProperty] private bool _webpLossless;

    /// <summary>WebP effort 0 (fast) … 6 (smallest).</summary>
    [ObservableProperty] private int _webpMethod = 4;

    /// <summary>AVIF encoder speed 0 (slowest, smallest) … 9 (fastest).</summary>
    [ObservableProperty] private int _avifSpeed = 6;

    [ObservableProperty] private TiffCompression _tiffCompression = TiffCompression.Lzw;
    [ObservableProperty] private bool _gifDither = true;

    // ---------------------------------------------------------------- Game textures
    [ObservableProperty] private DdsCompression _ddsCompression = DdsCompression.Bc3;

    /// <summary>DDS: write the full mipmap chain.</summary>
    [ObservableProperty] private bool _ddsMipmaps = true;

    /// <summary>TGA: run-length encoding (lossless, smaller).</summary>
    [ObservableProperty] private bool _tgaRle = true;

    /// <summary>Scale each side to a power of two (256, 512, 1024 …) after resizing.</summary>
    [ObservableProperty] private PowerOfTwo _powerOfTwo = PowerOfTwo.Off;

    /// <summary>Pixel art: enlarge by a whole factor without smoothing (nearest neighbour). 1 = off.</summary>
    [ObservableProperty] private int _pixelScale = 1;

    /// <summary>Game textures: also write a normal map "name_n.png" (2D lighting, e.g. Godot CanvasTexture).</summary>
    [ObservableProperty] private bool _normalMap;

    /// <summary>Normal map strength (bumpiness), 0.5–10.</summary>
    [ObservableProperty] private double _normalStrength = 2;

    /// <summary>Normal map green channel down (DirectX/Unreal convention); off = OpenGL/Godot/Unity convention.</summary>
    [ObservableProperty] private bool _normalInvertY;

    /// <summary>Animated GIF/WebP sources stay animated when the target supports it.</summary>
    [ObservableProperty] private bool _keepAnimation = true;

    // ---------------------------------------------------------------- Size
    [ObservableProperty] private ResizeMode _resize = ResizeMode.Off;
    [ObservableProperty] private int _width = 1920;
    [ObservableProperty] private int _height = 1080;
    [ObservableProperty] private int _percent = 50;
    [ObservableProperty] private ScaleMode _exactMode = ScaleMode.Fit;
    [ObservableProperty] private bool _noUpscale = true;

    // ---------------------------------------------------------------- Metadata
    /// <summary>Rotate according to the EXIF orientation (always before metadata is removed).</summary>
    [ObservableProperty] private bool _autoOrient = true;

    [ObservableProperty] private bool _stripMetadata;
}
