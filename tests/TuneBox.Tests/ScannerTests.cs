namespace TuneBox.Tests;

public sealed class FilenameParserTests
{
    [Fact]
    public void ParsesArtistDashTitle()
    {
        var tags = FilenameParser.Parse("/music/Nina Simone - Feeling Good.mp3");
        Assert.Equal("Nina Simone", tags.Artist);
        Assert.Equal("Feeling Good", tags.Title);
    }

    [Fact]
    public void ParsesNumberedArtistDashTitle()
    {
        var tags = FilenameParser.Parse("/music/04 - Daft Punk - Around the World.mp3");
        Assert.Equal(4, tags.TrackNumber);
        Assert.Equal("Daft Punk", tags.Artist);
        Assert.Equal("Around the World", tags.Title);
    }

    [Fact]
    public void BareNameBecomesTitle()
    {
        var tags = FilenameParser.Parse("/music/Some Song.mp3");
        Assert.Equal("Some Song", tags.Title);
        Assert.Null(tags.Artist);
    }
}

public sealed class ScannerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "tunebox-tests-" + Guid.NewGuid().ToString("N")[..8]);

    public ScannerTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private string WriteFile(string name, byte[] bytes)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [Fact]
    public void ScanAddsTaggedTracks()
    {
        WriteFile("Miles Davis - So What.mp3", Mp3Fixture.WithV2(
            title: "So What", artist: "Miles Davis", album: "Kind of Blue", year: "1959"));
        WriteFile("notes.txt", "not audio"u8.ToArray());

        var library = new Library();
        var result = AudioScanner.ScanInto(library, _dir, recursive: false);

        Assert.Equal(1, result.Added);
        Assert.Equal(0, result.Skipped);
        Assert.Equal("So What", library.Tracks[0].Title);
        Assert.Equal("Miles Davis", library.Tracks[0].Artist);
        Assert.Equal("mp3", library.Tracks[0].Format);
    }

    [Fact]
    public void RescanIsIdempotent()
    {
        WriteFile("a.mp3", Mp3Fixture.WithV2(title: "A", artist: "X"));

        var library = new Library();
        var first = AudioScanner.ScanInto(library, _dir, recursive: false);
        var second = AudioScanner.ScanInto(library, _dir, recursive: false);

        Assert.Equal(1, first.Added);
        Assert.Equal(0, second.Added);
        Assert.Equal(1, second.Unchanged);
        Assert.Single(library.Tracks);
    }

    [Fact]
    public void SameRecordingAtNewPathUpdatesInsteadOfDuplicating()
    {
        var bytes = Mp3Fixture.WithV2(title: "Same Song", artist: "Same Artist");
        var oldPath = WriteFile("old.mp3", bytes);
        var library = new Library();
        AudioScanner.ScanInto(library, _dir, recursive: false);

        File.Delete(oldPath);
        WriteFile("new-name.mp3", bytes);
        var result = AudioScanner.ScanInto(library, _dir, recursive: false);

        Assert.Equal(1, result.Updated);
        Assert.Single(library.Tracks);
        Assert.EndsWith("new-name.mp3", library.Tracks[0].Path);
    }

    [Fact]
    public void RetaggingDoesNotCreateADuplicate()
    {
        var first = WriteFile("song.mp3", Mp3Fixture.WithV2(title: "First Title", artist: "A"));
        var library = new Library();
        AudioScanner.ScanInto(library, _dir, recursive: false);

        // Same audio payload, different title tag: only the tag bytes differ,
        // so the audio fingerprint is unchanged.
        File.WriteAllBytes(first, Mp3Fixture.WithV2(title: "Second Title", artist: "A"));
        var result = AudioScanner.ScanInto(library, _dir, recursive: false);

        Assert.Equal(0, result.Added);
        Assert.Single(library.Tracks);
    }

    [Fact]
    public void NonMp3FallsBackToFilename()
    {
        WriteFile("Cool Band - Cool Track.flac", new byte[] { (byte)'f', (byte)'L', (byte)'a', (byte)'C', 1, 2, 3 });

        var library = new Library();
        AudioScanner.ScanInto(library, _dir, recursive: false);

        Assert.Single(library.Tracks);
        Assert.Equal("Cool Band", library.Tracks[0].Artist);
        Assert.Equal("Cool Track", library.Tracks[0].Title);
        Assert.Equal("flac", library.Tracks[0].Format);
    }

    [Fact]
    public void RecursiveScanFindsNestedFiles()
    {
        var sub = Path.Combine(_dir, "deep");
        Directory.CreateDirectory(sub);
        File.WriteAllBytes(Path.Combine(sub, "deep.mp3"), Mp3Fixture.WithV2(title: "Deep", artist: "X"));

        var library = new Library();
        var flat = AudioScanner.ScanInto(library, _dir, recursive: false);
        Assert.Equal(0, flat.Added);
        var deep = AudioScanner.ScanInto(library, _dir, recursive: true);
        Assert.Equal(1, deep.Added);
    }
}

public sealed class LibraryStoreTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), "tunebox-lib-" + Guid.NewGuid().ToString("N")[..8] + ".json");

    public void Dispose()
    {
        if (File.Exists(_file))
            File.Delete(_file);
    }

    [Fact]
    public void MissingFileLoadsEmptyLibrary()
    {
        var library = new LibraryStore(_file).Load();
        Assert.Empty(library.Tracks);
        Assert.Empty(library.Playlists);
    }

    [Fact]
    public void SaveAndLoadRoundTrips()
    {
        var store = new LibraryStore(_file);
        var library = new Library();
        library.Tracks.Add(new Track
        {
            Id = "abc", Path = "/music/a.mp3", Title = "A", Artist = "X",
            Format = "mp3", FileSize = 123, AddedAt = DateTimeOffset.UtcNow,
        });
        library.Playlists.Add(new Playlist { Name = "Mix", CreatedAt = DateTimeOffset.UtcNow });
        store.Save(library);

        var loaded = store.Load();
        Assert.Single(loaded.Tracks);
        Assert.Equal("A", loaded.Tracks[0].Title);
        Assert.Single(loaded.Playlists);
        Assert.Equal("Mix", loaded.Playlists[0].Name);
    }
}
