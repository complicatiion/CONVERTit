using ImageMagick;

namespace CONVERTit.Engines.Image;

/// <summary>Small conversions of in-memory images (thumbnail cache viewer).</summary>
public static class ImageBytes
{
    /// <summary>Any image Magick reads → PNG (alpha kept).</summary>
    public static byte[] ToPng(byte[] data)
    {
        using var image = new MagickImage(data);
        image.Format = MagickFormat.Png;
        return image.ToByteArray();
    }

    /// <summary>
    /// Clipboard formats: a 24-bit BMP file (CF_DIB, transparency flattened on white) and a PNG (with transparency).
    /// </summary>
    public static (byte[] Bmp, byte[] Png) ForClipboard(byte[] data)
    {
        using var image = new MagickImage(data);
        image.Format = MagickFormat.Png;
        var png = image.ToByteArray();
        using var flat = image.Clone();
        flat.BackgroundColor = MagickColors.White;
        flat.Alpha(AlphaOption.Remove);
        flat.Format = MagickFormat.Bmp3;
        return (flat.ToByteArray(), png);
    }
}
