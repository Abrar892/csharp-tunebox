namespace TuneBox.Tests;

public sealed class QueryTests
{
    private static Track T(string id, string? title, string? artist, string? album = null, string? genre = null, int? year = null) =>
        new()
        {
            Id = id, Path = $"/music/{id}.mp3", Title = title, Artist = artist,
            Album = album, Genre = genre, Year = year, Format = "mp3",
            FileSize = 100, AddedAt = DateTimeOffset.UtcNow,
        };

    private static readonly List<Track> Tracks =
    [
        T("1", "Blue in Green", "Miles Davis", "Kind of Blue", "Jazz", 1959),
        T("2", "So What", "Miles Davis", "Kind of Blue", "Jazz", 1959),
        T("3", "Blue Monday", "New Order", "Power, Corruption & Lies", "New Wave", 1983),
        T("4", "Green Onions", "Booker T. & the M.G.'s", "Green Onions", "Soul", 1962),
    ];

    [Fact]
    public void SearchMatchesAcrossFields()
    {
        var results = LibraryQuery.Search(Tracks, "Miles");
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public void SearchRequiresEveryWord()
    {
        // "Green" matches only track 1's title/album; "Miles" matches the
        // artist of tracks 1 and 2. Only track 1 satisfies both words.
        var results = LibraryQuery.Search(Tracks, "Green Miles");
        Assert.Single(results);
        Assert.Equal("Blue in Green", results[0].Title);
    }

    [Fact]
    public void SearchWordsMayMatchDifferentFields()
    {
        // "Blue" matches track 2's album, "Miles" its artist.
        var results = LibraryQuery.Search(Tracks, "Blue Miles");
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public void SearchIsCaseInsensitive()
    {
        Assert.Single(LibraryQuery.Search(Tracks, "blue monday"));
    }

    [Fact]
    public void SearchRanksTitleMatchesFirst()
    {
        // "Blue" hits track 1's title and album (score 5), track 3's title
        // (score 3), and track 2's album only (score 2).
        var results = LibraryQuery.Search(Tracks, "Blue");
        Assert.Equal(3, results.Count);
        Assert.Equal("Blue in Green", results[0].Title);
        Assert.Equal("Blue Monday", results[1].Title);
        Assert.Equal("So What", results[2].Title);
    }

    [Fact]
    public void SearchWithNoMatchReturnsEmpty()
    {
        Assert.Empty(LibraryQuery.Search(Tracks, "Zebra"));
    }

    [Fact]
    public void FilterByArtistAndAlbum()
    {
        var byArtist = LibraryQuery.Filter(Tracks, "Miles Davis", null);
        Assert.Equal(2, byArtist.Count);

        var byAlbum = LibraryQuery.Filter(Tracks, null, "Kind of Blue");
        Assert.Equal(2, byAlbum.Count);

        var both = LibraryQuery.Filter(Tracks, "Miles Davis", "Green Onions");
        Assert.Empty(both);
    }

    [Fact]
    public void FilterSortsByArtistThenAlbumThenTrackNumber()
    {
        var results = LibraryQuery.Filter(Tracks, null, null);
        Assert.Equal("Booker T. & the M.G.'s", results[0].Artist);
        Assert.Equal("Miles Davis", results[1].Artist);
    }
}

public sealed class StatsAndDuplicateTests
{
    private static Track T(string id, string? title, string? artist, long size = 1000, string format = "mp3", int? year = null, TimeSpan? duration = null) =>
        new()
        {
            Id = id, Path = $"/music/{id}.mp3", Title = title, Artist = artist,
            Format = format, FileSize = size, Year = year, Duration = duration,
            AddedAt = DateTimeOffset.UtcNow,
        };

    [Fact]
    public void StatsTotalsAreCorrect()
    {
        var tracks = new[]
        {
            T("1", "A", "X", size: 1000, year: 1990, duration: TimeSpan.FromMinutes(3)),
            T("2", "B", "X", size: 2000, year: 2000, duration: TimeSpan.FromMinutes(4)),
            T("3", "C", "Y", size: 500, format: "flac"),
        };
        var stats = LibraryStats.Compute(tracks);

        Assert.Equal(3, stats.TrackCount);
        Assert.Equal(3500, stats.TotalBytes);
        Assert.Equal(TimeSpan.FromMinutes(7), stats.TotalDuration);
        Assert.Equal(2, stats.TracksWithDuration);
        Assert.Equal(1990, stats.EarliestYear);
        Assert.Equal(2000, stats.LatestYear);
        Assert.Equal("X", stats.TopArtists[0].Key);
        Assert.Equal(2, stats.TopArtists[0].Value);
        Assert.Equal(2, stats.Formats.Count);
    }

    [Fact]
    public void StatsOnEmptyLibrary()
    {
        var stats = LibraryStats.Compute(Array.Empty<Track>());
        Assert.Equal(0, stats.TrackCount);
        Assert.Equal(0, stats.TotalBytes);
        Assert.Null(stats.EarliestYear);
    }

    [Fact]
    public void DuplicateFinderGroupsSameTitleAndArtist()
    {
        var tracks = new[]
        {
            T("a1", "Same Song", "Same Artist", size: 1000),
            T("a2", "Same Song", "Same Artist", size: 2000),
            T("b1", "Other Song", "Same Artist", size: 1000),
        };
        var groups = DuplicateFinder.Find(tracks);
        Assert.Single(groups);
        Assert.Equal(2, groups[0].Count);
    }

    [Fact]
    public void DuplicateFinderIgnoresCaseAndPunctuation()
    {
        var tracks = new[]
        {
            T("a1", "Don't Stop", "The Band"),
            T("a2", "dont stop!", "THE BAND"),
        };
        Assert.Single(DuplicateFinder.Find(tracks));
    }

    [Fact]
    public void DuplicateFinderIgnoresByteIdenticalTracks()
    {
        // Same fingerprint = already merged by the scanner, not a duplicate.
        var tracks = new[]
        {
            T("same", "Song", "Artist"),
            T("same", "Song", "Artist"),
        };
        Assert.Empty(DuplicateFinder.Find(tracks));
    }

    [Fact]
    public void NormalizeStripsPunctuation()
    {
        Assert.Equal("dontstop", DuplicateFinder.Normalize("Don't Stop!"));
        Assert.Equal("blueingreen", DuplicateFinder.Normalize("Blue in Green"));
    }
}

public sealed class PlaylistTests
{
    private static Track T(string id, string title, string artist) =>
        new()
        {
            Id = id, Path = $"/music/{id}.mp3", Title = title, Artist = artist,
            Format = "mp3", FileSize = 100, Duration = TimeSpan.FromSeconds(180),
            AddedAt = DateTimeOffset.UtcNow,
        };

    private static Library LibraryWith(params Track[] tracks)
    {
        var library = new Library();
        library.Tracks.AddRange(tracks);
        return library;
    }

    [Fact]
    public void CreateRejectsDuplicateName()
    {
        var library = LibraryWith();
        PlaylistService.Create(library, "Road Trip");
        Assert.Throws<InvalidOperationException>(() => PlaylistService.Create(library, "road trip"));
    }

    [Fact]
    public void AddBySearchAddsMatchesOnce()
    {
        var library = LibraryWith(T("1", "Blue in Green", "Miles Davis"), T("2", "So What", "Miles Davis"));
        var playlist = PlaylistService.Create(library, "Jazz");
        var added = PlaylistService.AddBySearch(library, playlist, "Miles");
        Assert.Equal(2, added);
        Assert.Equal(0, PlaylistService.AddBySearch(library, playlist, "Miles"));
        Assert.Equal(2, playlist.TrackIds.Count);
    }

    [Fact]
    public void RemoveAtRemovesByPosition()
    {
        var library = LibraryWith(T("1", "A", "X"), T("2", "B", "X"), T("3", "C", "X"));
        var playlist = PlaylistService.Create(library, "P");
        PlaylistService.AddBySearch(library, playlist, "X");
        var removed = PlaylistService.RemoveAt(library, playlist, 2);
        Assert.Equal("B", removed.Title);
        Assert.Equal(2, playlist.TrackIds.Count);
        Assert.Throws<ArgumentOutOfRangeException>(() => PlaylistService.RemoveAt(library, playlist, 5));
    }

    [Fact]
    public void M3uExportHasHeaderAndExtinfLines()
    {
        var library = LibraryWith(T("1", "A", "X"), T("2", "B", "Y"));
        var playlist = PlaylistService.Create(library, "P");
        PlaylistService.AddBySearch(library, playlist, "X");

        var file = Path.Combine(Path.GetTempPath(), "tunebox-" + Guid.NewGuid().ToString("N")[..8] + ".m3u");
        try
        {
            M3uExporter.Export(library, playlist, file);
            var lines = File.ReadAllLines(file);
            Assert.Equal("#EXTM3U", lines[0]);
            Assert.Equal("#EXTINF:180,X - A", lines[1]);
            Assert.EndsWith("1.mp3", lines[2]);
            Assert.Equal(3, lines.Length);
        }
        finally
        {
            if (File.Exists(file))
                File.Delete(file);
        }
    }
}
