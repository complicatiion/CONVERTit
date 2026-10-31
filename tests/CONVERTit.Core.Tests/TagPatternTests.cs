using CONVERTit.Core.Tags;
using Xunit;

namespace CONVERTit.Core.Tests;

public class TagPatternTests
{
    [Fact]
    public void Parses_track_artist_title()
    {
        var v = TagPattern.Parse("01 - Daft Punk - One More Time", TagPattern.DefaultPattern);
        Assert.Equal("1", v[TagField.Track]);
        Assert.Equal("Daft Punk", v[TagField.Artist]);
        Assert.Equal("One More Time", v[TagField.Title]);
    }

    [Fact]
    public void Non_matching_name_gives_nothing()
    {
        Assert.Empty(TagPattern.Parse("random_name", "%artist% - %title%"));
    }

    [Fact]
    public void Formats_with_padded_track_and_clean_characters()
    {
        var name = TagPattern.Format(TagPattern.DefaultPattern, new Dictionary<TagField, string>
        {
            [TagField.Track] = "3",
            [TagField.Artist] = "AC/DC",
            [TagField.Title] = "T.N.T?"
        });
        Assert.Equal("03 - AC_DC - T.N.T_", name);
    }

    [Fact]
    public void Empty_fields_do_not_leave_separators()
    {
        var name = TagPattern.Format(TagPattern.DefaultPattern, new Dictionary<TagField, string> { [TagField.Title] = "Intro" });
        Assert.Equal("Intro", name);
    }

    [Fact]
    public void Title_case_and_spaces()
    {
        Assert.Equal("The Dark Side Of The Moon", TagPattern.TitleCase("the dark  side OF the moon".Replace("  ", " ")));
        Assert.Equal("a b", TagPattern.CleanSpaces("  a   b "));
    }
}
