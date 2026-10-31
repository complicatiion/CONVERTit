using CONVERTit.Core.Tags;
using TagLib;

namespace CONVERTit.Engines.Tags;

/// <summary>Tag values of one file (strings for text, numbers as text; empty = not set).</summary>
public sealed class TagData
{
    public Dictionary<TagField, string> Values { get; } = new();

    public byte[]? Cover { get; set; }

    public string CoverMime { get; set; } = "image/jpeg";

    /// <summary>Tag types on disk, e.g. "ID3v2.3 · ID3v1".</summary>
    public string TagTypes { get; set; } = "";

    public string Get(TagField field) => Values.TryGetValue(field, out var v) ? v : "";
}

/// <summary>Changes to write: only fields in <see cref="Values"/> are touched (empty string = remove).</summary>
public sealed class TagEdit
{
    public Dictionary<TagField, string> Values { get; } = new();

    /// <summary>New cover (JPEG/PNG bytes) when <see cref="CoverChanged"/>; null = remove the cover.</summary>
    public byte[]? Cover { get; set; }

    public string CoverMime { get; set; } = "image/jpeg";

    public bool CoverChanged { get; set; }

    public bool IsEmpty => Values.Count == 0 && !CoverChanged;
}

/// <summary>How tags are written.</summary>
public sealed class TagWriteOptions
{
    /// <summary>ID3v2 version for MP3 (3 = v2.3, best compatibility with Windows and car radios; 4 = v2.4).</summary>
    public int Id3Version { get; set; } = 3;

    /// <summary>MP3: also write an ID3v1 tag (old players); off removes an existing one.</summary>
    public bool WriteId3v1 { get; set; }

    /// <summary>Keep the file's modification date.</summary>
    public bool KeepFileDate { get; set; } = true;
}

/// <summary>
/// Reads and writes audio tags with TagLib#: MP3 (ID3v1, ID3v2.3/2.4), FLAC, OGG Vorbis, Opus (Vorbis comments),
/// M4A/M4B (MP4 atoms), WAV and AIFF (ID3v2). Tags are written in place; a backup copy protects the file until
/// the write succeeded. Audio data is never touched.
/// </summary>
public static class TagEngine
{
    static TagEngine()
    {
        // New ID3v2 tags default to v2.3 (UTF-16 text): read by Windows Explorer, car radios and older players.
        TagLib.Id3v2.Tag.DefaultVersion = 3;
        TagLib.Id3v2.Tag.ForceDefaultVersion = false;
    }

    public static readonly IReadOnlySet<string> Extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "mp3", "flac", "ogg", "oga", "opus", "m4a", "m4b", "wav", "aif", "aiff"
    };

    public static bool Supports(string path) => Extensions.Contains(Path.GetExtension(path).TrimStart('.'));

    public static TagData Read(string path)
    {
        using var file = TagLib.File.Create(path);
        var tag = file.Tag;
        var data = new TagData();
        Set(data, TagField.Title, tag.Title);
        Set(data, TagField.Artist, Join(tag.Performers));
        Set(data, TagField.Album, tag.Album);
        Set(data, TagField.AlbumArtist, Join(tag.AlbumArtists));
        Set(data, TagField.Composer, Join(tag.Composers));
        Set(data, TagField.Genre, Join(tag.Genres));
        Set(data, TagField.Comment, tag.Comment);
        Set(data, TagField.Year, tag.Year > 0 ? tag.Year.ToString() : "");
        Set(data, TagField.Track, tag.Track > 0 ? tag.Track.ToString() : "");
        Set(data, TagField.TrackCount, tag.TrackCount > 0 ? tag.TrackCount.ToString() : "");
        Set(data, TagField.Disc, tag.Disc > 0 ? tag.Disc.ToString() : "");
        Set(data, TagField.DiscCount, tag.DiscCount > 0 ? tag.DiscCount.ToString() : "");

        var picture = tag.Pictures?.FirstOrDefault(p => p.Type == PictureType.FrontCover) ?? tag.Pictures?.FirstOrDefault();
        if (picture is not null && picture.Data.Count > 0)
        {
            data.Cover = picture.Data.Data;
            data.CoverMime = string.IsNullOrEmpty(picture.MimeType) ? "image/jpeg" : picture.MimeType;
        }

        data.TagTypes = Describe(file);
        return data;
    }

    /// <summary>Writes the changes; on any error the original file is restored from the backup.</summary>
    public static void Write(string path, TagEdit edit, TagWriteOptions options)
    {
        if (edit.IsEmpty && !IsMp3(path))
        {
            return;
        }

        var modified = System.IO.File.GetLastWriteTime(path);
        var backup = path + ".cvtbak";
        System.IO.File.Copy(path, backup, overwrite: true);
        try
        {
            using (var file = TagLib.File.Create(path))
            {
                // MP3, WAV and AIFF carry ID3v2: written in the chosen version (default v2.3).
                if (UsesId3(path) && file.GetTag(TagLib.TagTypes.Id3v2, true) is TagLib.Id3v2.Tag v2)
                {
                    v2.Version = (byte)(options.Id3Version == 4 ? 4 : 3);
                }

                if (IsMp3(path))
                {
                    if (options.WriteId3v1)
                    {
                        file.GetTag(TagLib.TagTypes.Id3v1, true);
                    }
                    else
                    {
                        file.RemoveTags(TagLib.TagTypes.Id3v1);
                    }
                }

                Apply(file.Tag, edit);
                file.Save();
            }

            if (options.KeepFileDate)
            {
                System.IO.File.SetLastWriteTime(path, modified);
            }

            System.IO.File.Delete(backup);
        }
        catch
        {
            // Restore the untouched original.
            System.IO.File.Copy(backup, path, overwrite: true);
            System.IO.File.Delete(backup);
            throw;
        }
    }

    private static void Apply(Tag tag, TagEdit edit)
    {
        foreach (var (field, raw) in edit.Values)
        {
            var value = raw.Trim();
            switch (field)
            {
                case TagField.Title: tag.Title = Null(value); break;
                case TagField.Artist: tag.Performers = Split(value); break;
                case TagField.Album: tag.Album = Null(value); break;
                case TagField.AlbumArtist: tag.AlbumArtists = Split(value); break;
                case TagField.Composer: tag.Composers = Split(value); break;
                case TagField.Genre: tag.Genres = Split(value); break;
                case TagField.Comment: tag.Comment = Null(value); break;
                case TagField.Year: tag.Year = Number(value); break;
                case TagField.Track: tag.Track = Number(value); break;
                case TagField.TrackCount: tag.TrackCount = Number(value); break;
                case TagField.Disc: tag.Disc = Number(value); break;
                case TagField.DiscCount: tag.DiscCount = Number(value); break;
            }
        }

        if (edit.CoverChanged)
        {
            tag.Pictures = edit.Cover is { Length: > 0 } bytes
                ? [new Picture(new ByteVector(bytes)) { Type = PictureType.FrontCover, MimeType = edit.CoverMime, Description = "Cover" }]
                : [];
        }
    }

    private static bool UsesId3(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".mp3" or ".wav" or ".aif" or ".aiff";

    private static bool IsMp3(string path) => Path.GetExtension(path).Equals(".mp3", StringComparison.OrdinalIgnoreCase);

    private static string Describe(TagLib.File file)
    {
        var parts = new List<string>();
        var types = file.TagTypesOnDisk;
        if (types.HasFlag(TagLib.TagTypes.Id3v2) && file.GetTag(TagLib.TagTypes.Id3v2, false) is TagLib.Id3v2.Tag v2)
        {
            parts.Add($"ID3v2.{v2.Version}");
        }

        if (types.HasFlag(TagLib.TagTypes.Id3v1)) parts.Add("ID3v1");
        if (types.HasFlag(TagLib.TagTypes.Ape)) parts.Add("APE");
        if (types.HasFlag(TagLib.TagTypes.Xiph)) parts.Add("Vorbis comment");
        if (types.HasFlag(TagLib.TagTypes.Apple)) parts.Add("MP4");
        if (types.HasFlag(TagLib.TagTypes.RiffInfo)) parts.Add("RIFF INFO");
        return parts.Count > 0 ? string.Join(" · ", parts) : "–";
    }

    private static void Set(TagData data, TagField field, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            data.Values[field] = value.Trim();
        }
    }

    private static string Join(string[]? values) => values is { Length: > 0 } ? string.Join("; ", values) : "";

    private static string[] Split(string value) =>
        value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string? Null(string value) => value.Length > 0 ? value : null;

    private static uint Number(string value) => uint.TryParse(value, out var n) ? n : 0;
}
