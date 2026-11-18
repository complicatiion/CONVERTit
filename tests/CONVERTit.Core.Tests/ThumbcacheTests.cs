using System.Buffers.Binary;
using System.Text;
using CONVERTit.Core.Files;
using Xunit;

namespace CONVERTit.Core.Tests;

public class ThumbcacheTests
{
    private static readonly byte[] Png =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52,
        0, 0, 0, 96, 0, 0, 0, 64, 8, 6, 0, 0, 0
    ];

    /// <summary>Win10 cache: header (24 bytes, first entry at 16), one empty entry, one PNG entry.</summary>
    private static byte[] Win10Cache()
    {
        var file = new List<byte>();
        var header = new byte[24];
        "CMMM"u8.CopyTo(header);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4), 0x20);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(16), 24);
        file.AddRange(header);
        file.AddRange(Entry([], 0, 0, "aaaa"));
        file.AddRange(Entry(Png, 96, 64, "bbbb"));
        return file.ToArray();
    }

    private static byte[] Entry(byte[] payload, int width, int height, string id)
    {
        var idBytes = Encoding.Unicode.GetBytes(id);
        var size = 56 + idBytes.Length + payload.Length;
        var e = new byte[size];
        "CMMM"u8.CopyTo(e);
        BinaryPrimitives.WriteInt32LittleEndian(e.AsSpan(4), size);
        BinaryPrimitives.WriteUInt64LittleEndian(e.AsSpan(8), 0x1122334455667788);
        BinaryPrimitives.WriteInt32LittleEndian(e.AsSpan(16), idBytes.Length);
        BinaryPrimitives.WriteInt32LittleEndian(e.AsSpan(20), 0);
        BinaryPrimitives.WriteInt32LittleEndian(e.AsSpan(24), payload.Length);
        BinaryPrimitives.WriteInt32LittleEndian(e.AsSpan(28), width);
        BinaryPrimitives.WriteInt32LittleEndian(e.AsSpan(32), height);
        idBytes.CopyTo(e, 56);
        payload.CopyTo(e, 56 + idBytes.Length);
        return e;
    }

    [Fact]
    public void Win10_cache_entries_with_data_are_listed()
    {
        var data = Win10Cache();
        var entries = ThumbcacheReader.Parse(data, "thumbcache_96.db");
        var entry = Assert.Single(entries);
        Assert.Equal("png", entry.Format);
        Assert.Equal(96, entry.Width);
        Assert.Equal(64, entry.Height);
        Assert.Equal("bbbb", entry.Id);
        Assert.True(ThumbcacheReader.Extract(data, entry).SequenceEqual(Png));
    }

    [Fact]
    public void Garbage_and_truncated_files_do_not_throw()
    {
        Assert.Empty(ThumbcacheReader.Parse([1, 2, 3], "x"));
        var data = Win10Cache();
        Assert.Empty(ThumbcacheReader.Parse(data[..60], "x"));
    }

    [Fact]
    public void Image_sizes_come_from_headers()
    {
        Assert.Equal((96, 64), ThumbcacheReader.Size(Png, "png"));
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 4, 0, 0, 0xFF, 0xC0, 0, 17, 8, 0, 120, 0, 160, 3];
        Assert.Equal("jpg", ThumbcacheReader.Detect(jpeg));
        Assert.Equal((160, 120), ThumbcacheReader.Size(jpeg, "jpg"));
    }

    [Fact]
    public void Thumbs_db_catalog_maps_numbers_to_names()
    {
        var catalog = new List<byte>();
        var header = new byte[16];
        BinaryPrimitives.WriteUInt16LittleEndian(header, 16);
        catalog.AddRange(header);
        var name = Encoding.Unicode.GetBytes("Urlaub.jpg\0");
        var entry = new byte[16 + name.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(entry, (uint)entry.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(entry.AsSpan(4), 12);
        name.CopyTo(entry, 16);
        catalog.AddRange(entry);
        var names = ThumbcacheReader.ParseCatalog(catalog.ToArray());
        Assert.Equal("Urlaub.jpg", names[12]);
        Assert.Equal(12, ThumbcacheReader.StreamNumber("21"));
        Assert.Equal(3, ThumbcacheReader.ImageStart([0, 0, 0, 0xFF, 0xD8, 0xFF, 0xE0]));
    }
}

public class CompoundFileTests
{
    private const uint End = 0xFFFFFFFE;
    private const uint Free = 0xFFFFFFFF;

    /// <summary>
    /// v3 file, 512-byte sectors: 0 = FAT, 1 = directory, 2 = mini FAT, 3 = mini stream (root), 4–13 = big stream.
    /// Streams: "Catalog" (100 bytes, mini), "1" (70 bytes, mini, 2 mini sectors), "Big" (5000 bytes, regular).
    /// </summary>
    public static byte[] Build(bool cyclicBig = false)
    {
        const int s = 512;
        var file = new byte[s * 15];
        byte[] sig = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];
        sig.CopyTo(file, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(0x18), 0x3E);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(0x1A), 3);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(0x1C), 0xFFFE);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(0x1E), 9);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(0x20), 6);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(0x2C), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(0x30), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(0x38), 4096);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(0x3C), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(0x40), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(0x44), End);
        for (var i = 0; i < 109; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(0x4C + i * 4), i == 0 ? 0 : Free);
        }

        Span<byte> Sector(int n) => file.AsSpan((n + 1) * s, s);

        // FAT
        var fat = Sector(0);
        uint[] entries = [0xFFFFFFFD, End, End, End, 5, 6, 7, 8, 9, 10, 11, 12, 13, cyclicBig ? 4u : End];
        for (var i = 0; i < s / 4; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(fat[(i * 4)..], i < entries.Length ? entries[i] : Free);
        }

        // Directory
        void Entry(int index, string name, byte type, uint start, uint size)
        {
            var e = Sector(1).Slice(index * 128, 128);
            var n = Encoding.Unicode.GetBytes(name + "\0");
            n.CopyTo(e);
            BinaryPrimitives.WriteUInt16LittleEndian(e[64..], (ushort)n.Length);
            e[66] = type;
            BinaryPrimitives.WriteUInt32LittleEndian(e[68..], Free);
            BinaryPrimitives.WriteUInt32LittleEndian(e[72..], Free);
            BinaryPrimitives.WriteUInt32LittleEndian(e[76..], Free);
            BinaryPrimitives.WriteUInt32LittleEndian(e[116..], start);
            BinaryPrimitives.WriteUInt32LittleEndian(e[120..], size);
        }

        Entry(0, "Root Entry", 5, 3, 3 * 64 + 64);
        Entry(1, "Catalog", 2, 0, 100);
        Entry(2, "1", 2, 2, 70);
        Entry(3, "Big", 2, 4, 5000);

        // Mini FAT: Catalog = mini 0 → 1, "1" = mini 2 → 3
        var miniFat = Sector(2);
        uint[] mini = [1, End, 3, End];
        for (var i = 0; i < s / 4; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(miniFat[(i * 4)..], i < mini.Length ? mini[i] : Free);
        }

        // Mini stream content
        var ms = Sector(3);
        for (var i = 0; i < 128; i++)
        {
            ms[i] = (byte)'C';
        }

        for (var i = 128; i < 256; i++)
        {
            ms[i] = (byte)'1';
        }

        for (var n = 4; n <= 13; n++)
        {
            Sector(n).Fill((byte)n);
        }

        return file;
    }

    [Fact]
    public void Streams_are_read_from_mini_and_regular_sectors()
    {
        var cfb = CompoundFileReader.Open(Build());
        Assert.Equal(3, cfb.Streams.Count);
        var catalog = cfb.Streams.First(x => x.Name == "Catalog");
        Assert.True(catalog.InMiniStream);
        var data = cfb.Read(catalog);
        Assert.Equal(100, data.Length);
        Assert.True(data.All(b => b == (byte)'C'));
        Assert.True(cfb.Read(cfb.Streams.First(x => x.Name == "1")).All(b => b == (byte)'1'));
        var big = cfb.Read(cfb.Streams.First(x => x.Name == "Big"));
        Assert.Equal(5000, big.Length);
        Assert.Equal(4, big[0]);
        Assert.Equal(13, big[4999]);
    }

    [Fact]
    public void Cyclic_chain_returns_empty_instead_of_hanging()
    {
        var cfb = CompoundFileReader.Open(Build(cyclicBig: true));
        var big = cfb.Streams.First(x => x.Name == "Big");
        var data = cfb.Read(big with { Size = 100_000 });
        Assert.Empty(data);
        Assert.False(CompoundFileReader.IsCompoundFile(new byte[600]));
    }
}
