using System.Buffers.Binary;
using System.Text;

namespace CONVERTit.Core.Files;

/// <summary>A thumbnail found in a Windows thumbnail cache.</summary>
public sealed record CachedThumbnail(
    string Id,
    string? Name,
    int Width,
    int Height,
    string Format,
    long Offset,
    int Length,
    string SourceFile);

/// <summary>
/// Read-only parser for the Windows Explorer thumbnail cache (thumbcache_*.db, Windows Vista–11, "CMMM" format).
/// The cache stores only image data and a 64-bit hash per entry – no file names or paths. Entries without data
/// (placeholders) are skipped. The file is never modified.
/// </summary>
public static class ThumbcacheReader
{
    private static readonly byte[] Signature = "CMMM"u8.ToArray();

    /// <summary>Format version in the file header: Vista 0x14, Win7 0x15, Win8 0x1A, Win8.1 0x1F, Win10/11 0x20.</summary>
    public static int? Version(ReadOnlySpan<byte> data) =>
        data.Length >= 8 && data[..4].SequenceEqual(Signature) ? BinaryPrimitives.ReadInt32LittleEndian(data[4..]) : null;

    public static List<CachedThumbnail> Read(string path)
    {
        // The cache is opened by Explorer; read with full sharing and parse a copy in memory.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var data = new byte[stream.Length];
        stream.ReadExactly(data);
        return Parse(data, path);
    }

    public static List<CachedThumbnail> Parse(byte[] data, string sourceFile)
    {
        var result = new List<CachedThumbnail>();
        if (Version(data) is not { } version)
        {
            return result;
        }

        // Header: Vista/7 → first entry offset at 12, Win8+ → at 16.
        var firstOffsetAt = version <= 0x15 ? 12 : 16;
        var offset = data.Length >= firstOffsetAt + 4 ? BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(firstOffsetAt)) : 0;
        if (offset <= 0 || offset >= data.Length)
        {
            offset = NextSignature(data, 4);
        }

        while (offset >= 0 && offset + 48 <= data.Length)
        {
            var span = data.AsSpan(offset);
            if (!span[..4].SequenceEqual(Signature))
            {
                offset = NextSignature(data, offset + 1);
                continue;
            }

            var entrySize = BinaryPrimitives.ReadInt32LittleEndian(span[4..]);
            var hash = BinaryPrimitives.ReadUInt64LittleEndian(span[8..]);
            int idSize, padSize, dataSize, width = 0, height = 0, headerSize;
            if (version == 0x14)
            {
                // Vista: 8-byte extension (UTF-16) after the hash.
                idSize = BinaryPrimitives.ReadInt32LittleEndian(span[24..]);
                padSize = BinaryPrimitives.ReadInt32LittleEndian(span[28..]);
                dataSize = BinaryPrimitives.ReadInt32LittleEndian(span[32..]);
                headerSize = 56;
            }
            else if (version == 0x15)
            {
                idSize = BinaryPrimitives.ReadInt32LittleEndian(span[16..]);
                padSize = BinaryPrimitives.ReadInt32LittleEndian(span[20..]);
                dataSize = BinaryPrimitives.ReadInt32LittleEndian(span[24..]);
                headerSize = 48;
            }
            else
            {
                idSize = BinaryPrimitives.ReadInt32LittleEndian(span[16..]);
                padSize = BinaryPrimitives.ReadInt32LittleEndian(span[20..]);
                dataSize = BinaryPrimitives.ReadInt32LittleEndian(span[24..]);
                width = BinaryPrimitives.ReadInt32LittleEndian(span[28..]);
                height = BinaryPrimitives.ReadInt32LittleEndian(span[32..]);
                headerSize = 56;
            }

            if (entrySize < headerSize || offset + entrySize > data.Length || idSize < 0 || padSize < 0 || dataSize < 0)
            {
                offset = NextSignature(data, offset + 4);
                continue;
            }

            var dataStart = offset + headerSize + idSize + padSize;
            if (dataSize > 0 && dataStart + dataSize <= offset + entrySize)
            {
                var payload = data.AsSpan(dataStart, dataSize);
                var format = Detect(payload);
                if (format is not null)
                {
                    if (width <= 0 || height <= 0)
                    {
                        (width, height) = Size(payload, format);
                    }

                    var id = idSize > 0 ? Encoding.Unicode.GetString(data, offset + headerSize, idSize).TrimEnd('\0') : hash.ToString("x16");
                    result.Add(new CachedThumbnail(id, null, width, height, format, dataStart, dataSize, sourceFile));
                }
            }

            offset += entrySize;
        }

        return result;
    }

    /// <summary>"jpg", "png" or "bmp" by magic bytes; null for anything else.</summary>
    public static string? Detect(ReadOnlySpan<byte> data) =>
        data.Length < 4 ? null
        : data[0] == 0xFF && data[1] == 0xD8 ? "jpg"
        : data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47 ? "png"
        : data[0] == 0x42 && data[1] == 0x4D ? "bmp"
        : null;

    /// <summary>Image size from PNG/BMP headers or JPEG SOF markers ((0, 0) when unknown).</summary>
    public static (int Width, int Height) Size(ReadOnlySpan<byte> data, string format)
    {
        try
        {
            switch (format)
            {
                case "png" when data.Length >= 24:
                    return (BinaryPrimitives.ReadInt32BigEndian(data[16..]), BinaryPrimitives.ReadInt32BigEndian(data[20..]));
                case "bmp" when data.Length >= 26:
                    return (BinaryPrimitives.ReadInt32LittleEndian(data[18..]), Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(data[22..])));
                case "jpg":
                    var i = 2;
                    while (i + 9 < data.Length)
                    {
                        if (data[i] != 0xFF)
                        {
                            i++;
                            continue;
                        }

                        var marker = data[i + 1];
                        var length = BinaryPrimitives.ReadUInt16BigEndian(data[(i + 2)..]);
                        if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
                        {
                            return (BinaryPrimitives.ReadUInt16BigEndian(data[(i + 7)..]), BinaryPrimitives.ReadUInt16BigEndian(data[(i + 5)..]));
                        }

                        i += 2 + length;
                    }

                    break;
            }
        }
        catch (ArgumentOutOfRangeException)
        {
            // Truncated header.
        }

        return (0, 0);
    }

    /// <summary>
    /// Image data of one entry. Thumbs.db streams carry a small header before the JPEG/PNG; everything before the
    /// first image signature is cut off.
    /// </summary>
    public static byte[] Extract(byte[] data, CachedThumbnail entry) => data.AsSpan((int)entry.Offset, entry.Length).ToArray();

    /// <summary>Offset of the image inside a Thumbs.db stream (-1 when none).</summary>
    public static int ImageStart(ReadOnlySpan<byte> stream)
    {
        for (var i = 0; i + 4 <= stream.Length && i < 256; i++)
        {
            if (Detect(stream[i..]) is "jpg" or "png")
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Thumbs.db (Windows 98–XP and network folders) "Catalog" stream: stream number → file name.
    /// Layout: header (ushort size, ushort version, uint count, uint width, uint height), then per entry
    /// uint length, uint number, 8-byte FILETIME, UTF-16 name with terminator.
    /// </summary>
    public static Dictionary<int, string> ParseCatalog(ReadOnlySpan<byte> catalog)
    {
        var names = new Dictionary<int, string>();
        if (catalog.Length < 16)
        {
            return names;
        }

        int offset = BinaryPrimitives.ReadUInt16LittleEndian(catalog);
        while (offset + 16 <= catalog.Length)
        {
            var length = (int)BinaryPrimitives.ReadUInt32LittleEndian(catalog[offset..]);
            if (length < 16 || offset + length > catalog.Length)
            {
                break;
            }

            var number = (int)BinaryPrimitives.ReadUInt32LittleEndian(catalog[(offset + 4)..]);
            var nameBytes = catalog.Slice(offset + 16, length - 16);
            var name = Encoding.Unicode.GetString(nameBytes).Split('\0')[0];
            if (name.Length > 0)
            {
                names[number] = name;
            }

            offset += length;
        }

        return names;
    }

    /// <summary>Thumbs.db stream names are the catalog number written backwards ("21" → 12).</summary>
    public static int? StreamNumber(string streamName)
    {
        var reversed = new string(streamName.Reverse().ToArray());
        return int.TryParse(reversed, out var n) ? n : null;
    }

    private static int NextSignature(byte[] data, int from)
    {
        if (from < 0 || from >= data.Length)
        {
            return -1;
        }

        var index = data.AsSpan(from).IndexOf(Signature);
        return index < 0 ? -1 : from + index;
    }
}
