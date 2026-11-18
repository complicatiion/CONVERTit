using CONVERTit.Core.Files;

namespace CONVERTit.Engines.Image;

/// <summary>A thumbnail with its image data.</summary>
public sealed record ThumbnailData(CachedThumbnail Entry, byte[] Data);

/// <summary>
/// Reads thumbnail caches without changing them: thumbcache_*.db (Windows Vista–11) and Thumbs.db
/// (Windows 98–XP, still written on network shares), a Compound File read with the hardened
/// <see cref="CompoundFileReader"/>.
/// </summary>
public static class ThumbsDbReader
{
    /// <summary>Upper limit so a damaged or huge cache cannot exhaust memory.</summary>
    public const int MaxEntries = 20_000;

    public static List<ThumbnailData> Read(string path, CancellationToken cancellationToken = default)
    {
        var name = Path.GetFileName(path);
        return name.StartsWith("thumbcache_", StringComparison.OrdinalIgnoreCase) || name.StartsWith("iconcache_", StringComparison.OrdinalIgnoreCase)
            ? ReadThumbcache(path, cancellationToken)
            : ReadThumbsDb(path, cancellationToken);
    }

    private static List<ThumbnailData> ReadThumbcache(string path, CancellationToken cancellationToken)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var data = new byte[stream.Length];
        stream.ReadExactly(data);
        var result = new List<ThumbnailData>();
        foreach (var entry in ThumbcacheReader.Parse(data, path).Take(MaxEntries))
        {
            cancellationToken.ThrowIfCancellationRequested();
            result.Add(new ThumbnailData(entry, ThumbcacheReader.Extract(data, entry)));
        }

        return result;
    }

    private static List<ThumbnailData> ReadThumbsDb(string path, CancellationToken cancellationToken)
    {
        // Copy into memory first: the file may be locked or hidden/system, and OpenMcdf must not write to it.
        byte[] bytes;
        using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            bytes = new byte[file.Length];
            file.ReadExactly(bytes);
        }

        var result = new List<ThumbnailData>();
        var compound = CompoundFileReader.Open(bytes);
        var catalog = compound.Streams.FirstOrDefault(x => x.Name.Equals("Catalog", StringComparison.OrdinalIgnoreCase));
        Dictionary<int, string> names = catalog is null ? [] : ThumbcacheReader.ParseCatalog(compound.Read(catalog));
        foreach (var s in compound.Streams.Where(x => !ReferenceEquals(x, catalog)).Take(MaxEntries))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var data = compound.Read(s);
            var start = ThumbcacheReader.ImageStart(data);
            if (start < 0)
            {
                continue;
            }

            var image = data.AsSpan(start).ToArray();
            var format = ThumbcacheReader.Detect(image)!;
            var (width, height) = ThumbcacheReader.Size(image, format);
            var number = ThumbcacheReader.StreamNumber(s.Name);
            var fileName = number is { } n && names.TryGetValue(n, out var found) ? found : null;
            result.Add(new ThumbnailData(new CachedThumbnail(s.Name, fileName, width, height, format, 0, image.Length, path), image));
        }

        return result;
    }
}
