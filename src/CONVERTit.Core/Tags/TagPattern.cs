using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CONVERTit.Core.Tags;

/// <summary>Editable tag fields (also the placeholders of name patterns: %title%, %artist% …).</summary>
public enum TagField { Title, Artist, Album, AlbumArtist, Year, Track, TrackCount, Disc, DiscCount, Genre, Composer, Comment }

/// <summary>
/// File name patterns like Mp3tag: "%track% - %artist% - %title%". Parse reads tag values from a file name,
/// Format builds a file name from tag values. Pure functions (unit-tested).
/// </summary>
public static class TagPattern
{
    public const string DefaultPattern = "%track% - %artist% - %title%";

    private static readonly Regex Placeholder = new("%(\\w+)%", RegexOptions.Compiled);

    public static TagField? FieldOf(string name) => name.ToLowerInvariant() switch
    {
        "title" => TagField.Title,
        "artist" => TagField.Artist,
        "album" => TagField.Album,
        "albumartist" => TagField.AlbumArtist,
        "year" => TagField.Year,
        "track" => TagField.Track,
        "disc" => TagField.Disc,
        "genre" => TagField.Genre,
        "composer" => TagField.Composer,
        "comment" => TagField.Comment,
        _ => null
    };

    /// <summary>Values found in a file name (without extension); empty when the name does not match.</summary>
    public static Dictionary<TagField, string> Parse(string fileName, string pattern)
    {
        var result = new Dictionary<TagField, string>();
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return result;
        }

        var fields = new List<TagField?>();
        var regex = new StringBuilder("^");
        var last = 0;
        foreach (Match m in Placeholder.Matches(pattern))
        {
            regex.Append(Regex.Escape(pattern[last..m.Index]));
            var field = FieldOf(m.Groups[1].Value);
            fields.Add(field);
            // Numbers only take digits; text is non-greedy so separators split correctly.
            regex.Append(field is TagField.Track or TagField.Disc or TagField.Year ? "\\s*(\\d+)\\s*" : "(.*?)");
            last = m.Index + m.Length;
        }

        regex.Append(Regex.Escape(pattern[last..])).Append('$');
        var match = Regex.Match(fileName.Trim(), regex.ToString());
        if (!match.Success)
        {
            return result;
        }

        for (var i = 0; i < fields.Count; i++)
        {
            var value = match.Groups[i + 1].Value.Trim();
            if (fields[i] is { } field && value.Length > 0)
            {
                result[field] = field is TagField.Track or TagField.Disc
                    ? int.Parse(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)
                    : value;
            }
        }

        return result;
    }

    /// <summary>File name (without extension) from tag values; invalid file name characters are replaced.</summary>
    public static string Format(string pattern, IReadOnlyDictionary<TagField, string> values, int trackDigits = 2)
    {
        var name = Placeholder.Replace(pattern, m =>
        {
            if (FieldOf(m.Groups[1].Value) is not { } field || !values.TryGetValue(field, out var value) || string.IsNullOrWhiteSpace(value))
            {
                return "";
            }

            return field == TagField.Track && int.TryParse(value, out var n) ? n.ToString(new string('0', Math.Max(1, trackDigits)), CultureInfo.InvariantCulture) : value;
        });

        var invalid = Path.GetInvalidFileNameChars().Concat(['<', '>', ':', '"', '/', '\\', '|', '?', '*']).ToHashSet();
        var clean = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        // Separators left over from empty fields ("01 -  - Title") are collapsed.
        clean = Regex.Replace(clean, "\\s{2,}", " ");
        clean = Regex.Replace(clean, "^(\\s*-\\s*)+|(\\s*-\\s*)+$", "");
        clean = Regex.Replace(clean, "(\\s-\\s){2,}", " - ");
        return clean.Trim().TrimEnd('.');
    }

    /// <summary>"the dark side OF the moon" → "The Dark Side Of The Moon".</summary>
    public static string TitleCase(string? value) =>
        string.IsNullOrWhiteSpace(value) ? value ?? "" : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value.Trim().ToLowerInvariant());

    /// <summary>Removes doubled and surrounding spaces.</summary>
    public static string CleanSpaces(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "" : Regex.Replace(value.Trim(), "\\s{2,}", " ");
}

/// <summary>ID3v2 version written to MP3, WAV and AIFF files.</summary>
public enum Id3Version { V23, V24 }
