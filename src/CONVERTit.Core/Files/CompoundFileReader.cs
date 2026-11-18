using System.Buffers.Binary;
using System.Text;

namespace CONVERTit.Core.Files;

/// <summary>A stream inside a Compound File (OLE / CFB).</summary>
public sealed record CompoundStream(string Name, long Size, int StartSector, bool InMiniStream);

/// <summary>
/// Minimal read-only reader for Compound File Binary files ([MS-CFB], v3 and v4), enough for Thumbs.db.
/// Hardened against damaged or crafted files: the directory is read as a flat list (no tree walk, so cyclic
/// sibling links cannot cause endless loops), every sector chain is bounded by the number of sectors in the file
/// and checked for cycles, and all offsets are range-checked. Damaged parts are skipped instead of throwing.
/// </summary>
public sealed class CompoundFileReader
{
    private const uint EndOfChain = 0xFFFFFFFE;
    private const uint FreeSector = 0xFFFFFFFF;
    private const int MaxStreamSize = 64 * 1024 * 1024;

    private static readonly byte[] Signature = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

    private readonly byte[] _data;
    private readonly int _sectorSize;
    private readonly int _miniSectorSize;
    private readonly uint _miniCutoff;
    private readonly uint[] _fat;
    private readonly uint[] _miniFat;
    private readonly byte[] _miniStream;

    private CompoundFileReader(byte[] data)
    {
        _data = data;
        var shift = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(0x1E));
        var miniShift = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(0x20));
        if (shift is not (9 or 12) || miniShift != 6)
        {
            throw new InvalidDataException("Unsupported compound file sector size.");
        }

        _sectorSize = 1 << shift;
        _miniSectorSize = 1 << miniShift;
        _miniCutoff = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(0x38));
        _fat = ReadFat();

        var firstDirectory = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(0x30));
        var directory = ReadChain(firstDirectory, _fat, ReadSector, _sectorSize, long.MaxValue);
        Streams = ParseDirectory(directory, out var rootStart, out var rootSize);

        var firstMiniFat = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(0x3C));
        _miniFat = ToUInts(ReadChain(firstMiniFat, _fat, ReadSector, _sectorSize, long.MaxValue));
        _miniStream = rootSize > 0 ? ReadChain(rootStart, _fat, ReadSector, _sectorSize, Math.Min(rootSize, MaxStreamSize)) : [];
    }

    /// <summary>All streams of the file (any storage level), in directory order.</summary>
    public IReadOnlyList<CompoundStream> Streams { get; }

    public static bool IsCompoundFile(ReadOnlySpan<byte> data) => data.Length >= 512 && data[..8].SequenceEqual(Signature);

    /// <summary>Opens the file data; throws <see cref="InvalidDataException"/> when it is no compound file.</summary>
    public static CompoundFileReader Open(byte[] data)
    {
        if (!IsCompoundFile(data))
        {
            throw new InvalidDataException("No compound file (OLE) signature.");
        }

        return new CompoundFileReader(data);
    }

    /// <summary>Content of a stream (empty when its chain is damaged).</summary>
    public byte[] Read(CompoundStream stream)
    {
        var size = Math.Min(stream.Size, MaxStreamSize);
        try
        {
            return stream.InMiniStream
                ? ReadChain((uint)stream.StartSector, _miniFat, ReadMiniSector, _miniSectorSize, size)
                : ReadChain((uint)stream.StartSector, _fat, ReadSector, _sectorSize, size);
        }
        catch (InvalidDataException)
        {
            return [];
        }
    }

    private int SectorCount => Math.Max(0, _data.Length / _sectorSize - 1);

    private ReadOnlySpan<byte> ReadSector(uint sector)
    {
        var offset = ((long)sector + 1) * _sectorSize;
        if (sector >= int.MaxValue || offset + _sectorSize > _data.Length)
        {
            // The last sector may be cut short in files written by older tools.
            if (sector < int.MaxValue && offset < _data.Length)
            {
                return _data.AsSpan((int)offset);
            }

            throw new InvalidDataException("Sector outside the file.");
        }

        return _data.AsSpan((int)offset, _sectorSize);
    }

    private ReadOnlySpan<byte> ReadMiniSector(uint sector)
    {
        var offset = (long)sector * _miniSectorSize;
        if (offset + _miniSectorSize > _miniStream.Length)
        {
            throw new InvalidDataException("Mini sector outside the mini stream.");
        }

        return _miniStream.AsSpan((int)offset, _miniSectorSize);
    }

    /// <summary>Follows a sector chain; stops at the end marker, on a cycle or after as many steps as the table has entries.</summary>
    private static byte[] ReadChain(uint start, uint[] table, ReadSectorFunc read, int sectorSize, long size)
    {
        if (start is EndOfChain or FreeSector)
        {
            return [];
        }

        using var buffer = new MemoryStream();
        var visited = new HashSet<uint>();
        var sector = start;
        while (sector != EndOfChain && buffer.Length < size)
        {
            if (sector >= table.Length || !visited.Add(sector))
            {
                throw new InvalidDataException("Damaged sector chain.");
            }

            buffer.Write(read(sector));
            sector = table[sector];
        }

        var result = buffer.ToArray();
        return size < result.Length ? result.AsSpan(0, (int)size).ToArray() : result;
    }

    private delegate ReadOnlySpan<byte> ReadSectorFunc(uint sector);

    /// <summary>FAT from the 109 header entries plus the DIFAT chain (bounded, cycle-checked).</summary>
    private uint[] ReadFat()
    {
        var fatSectors = new List<uint>();
        for (var i = 0; i < 109; i++)
        {
            var s = BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(0x4C + i * 4));
            if (s < 0xFFFFFFFA)
            {
                fatSectors.Add(s);
            }
        }

        var difat = BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(0x44));
        var seen = new HashSet<uint>();
        var perSector = _sectorSize / 4;
        while (difat < 0xFFFFFFFA && seen.Add(difat) && seen.Count <= SectorCount)
        {
            var span = ReadSector(difat);
            for (var i = 0; i < perSector - 1 && (i + 1) * 4 <= span.Length; i++)
            {
                var s = BinaryPrimitives.ReadUInt32LittleEndian(span[(i * 4)..]);
                if (s < 0xFFFFFFFA)
                {
                    fatSectors.Add(s);
                }
            }

            difat = span.Length >= _sectorSize ? BinaryPrimitives.ReadUInt32LittleEndian(span[(_sectorSize - 4)..]) : EndOfChain;
        }

        var fat = new List<uint>(fatSectors.Count * perSector);
        foreach (var s in fatSectors.Take(SectorCount))
        {
            var span = ReadSector(s);
            for (var i = 0; i + 4 <= span.Length; i += 4)
            {
                fat.Add(BinaryPrimitives.ReadUInt32LittleEndian(span[i..]));
            }
        }

        return fat.ToArray();
    }

    private List<CompoundStream> ParseDirectory(byte[] directory, out uint rootStart, out long rootSize)
    {
        rootStart = EndOfChain;
        rootSize = 0;
        var streams = new List<CompoundStream>();
        for (var offset = 0; offset + 128 <= directory.Length; offset += 128)
        {
            var entry = directory.AsSpan(offset, 128);
            var type = entry[66];
            var start = BinaryPrimitives.ReadUInt32LittleEndian(entry[116..]);
            // Version 3 files may have garbage in the high 32 bits of the size.
            var size = _sectorSize == 512 ? BinaryPrimitives.ReadUInt32LittleEndian(entry[120..]) : (long)BinaryPrimitives.ReadUInt64LittleEndian(entry[120..]);
            if (type == 5)
            {
                rootStart = start;
                rootSize = size;
                continue;
            }

            if (type != 2 || size < 0)
            {
                continue;
            }

            var nameLength = Math.Clamp((int)BinaryPrimitives.ReadUInt16LittleEndian(entry[64..]), 0, 64);
            var name = Encoding.Unicode.GetString(entry[..Math.Max(0, nameLength - 2)]);
            streams.Add(new CompoundStream(name, size, (int)Math.Min(start, int.MaxValue), size < _miniCutoff));
        }

        return streams;
    }

    private static uint[] ToUInts(byte[] bytes)
    {
        var result = new uint[bytes.Length / 4];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(i * 4));
        }

        return result;
    }
}
