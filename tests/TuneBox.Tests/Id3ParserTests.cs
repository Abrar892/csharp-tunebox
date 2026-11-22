namespace TuneBox.Tests;

public sealed class Id3ParserTests
{
    [Fact]
    public void ParsesV2TextFrames()
    {
        var bytes = Mp3Fixture.WithV2(
            title: "Blue in Green", artist: "Miles Davis", album: "Kind of Blue",
            year: "1959", track: "3/5", genre: "Jazz");

        var tags = Id3Parser.Parse(new MemoryStream(bytes));

        Assert.Equal("Blue in Green", tags.Title);
        Assert.Equal("Miles Davis", tags.Artist);
        Assert.Equal("Kind of Blue", tags.Album);
        Assert.Equal(1959, tags.Year);
        Assert.Equal(3, tags.TrackNumber);
        Assert.Equal("Jazz", tags.Genre);
    }

    [Fact]
    public void ParsesNumericV2Genre()
    {
        var tags = Id3Parser.Parse(new MemoryStream(Mp3Fixture.WithV2(genre: "(17)")));
        Assert.Equal("Rock", tags.Genre);
    }

    [Fact]
    public void ParsesV1Tag()
    {
        var tags = Id3Parser.Parse(new MemoryStream(Mp3Fixture.WithV1(
            title: "Old Song", artist: "Old Artist", album: "Old Album",
            year: "1998", track: 5, genre: 7)));

        Assert.Equal("Old Song", tags.Title);
        Assert.Equal("Old Artist", tags.Artist);
        Assert.Equal("Old Album", tags.Album);
        Assert.Equal(1998, tags.Year);
        Assert.Equal(5, tags.TrackNumber);
        Assert.Equal("Hip-Hop", tags.Genre);
    }

    [Fact]
    public void V2WinsOverV1ButV1FillsGaps()
    {
        // v2 has a title only; v1 has everything else.
        var v2 = Mp3Fixture.WithV2(title: "New Title");
        var v1 = Mp3Fixture.WithV1(artist: "V1 Artist", album: "V1 Album", year: "2001");
        // Build a file with a v2 header followed by audio, then a v1 trailer.
        var audioLen = v2.Length;
        var bytes = new byte[audioLen + 128];
        Array.Copy(v2, bytes, audioLen);
        Array.Copy(v1[^128..], 0, bytes, audioLen, 128);

        var tags = Id3Parser.Parse(new MemoryStream(bytes));

        Assert.Equal("New Title", tags.Title);
        Assert.Equal("V1 Artist", tags.Artist);
        Assert.Equal("V1 Album", tags.Album);
        Assert.Equal(2001, tags.Year);
    }

    [Fact]
    public void FileWithoutAnyTagReturnsEmptyTags()
    {
        var tags = Id3Parser.Parse(new MemoryStream(new byte[] { 0xFF, 0xFB, 0x90, 0x00, 1, 2, 3 }));
        Assert.Null(tags.Title);
        Assert.Null(tags.Artist);
        Assert.Equal(128, tags.BitrateKbps);
    }

    [Fact]
    public void BitrateComesFromFirstMpegFrame()
    {
        var tags = Id3Parser.Parse(new MemoryStream(Mp3Fixture.WithV2(title: "X")));
        Assert.Equal(128, tags.BitrateKbps);
        Assert.True(tags.AudioByteCount >= 512);
    }

    [Fact]
    public void TruncatedV2TagDoesNotThrow()
    {
        var bytes = Mp3Fixture.WithV2(title: "Cut Short");
        var tags = Id3Parser.Parse(new MemoryStream(bytes[..20]));
        Assert.NotNull(tags);
    }
}
