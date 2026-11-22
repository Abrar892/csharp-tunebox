namespace TuneBox;

/// <summary>Playlist operations over a <see cref="Library"/>: create, add
/// tracks by search, remove by position, and export to M3U. Playlist names
/// are matched case-insensitively, and a track already in a playlist is
/// never added twice.</summary>
public static class PlaylistService
{
    public static Playlist? Find(Library library, string name) =>
        library.Playlists.FirstOrDefault(p =>
            string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    public static Playlist Create(Library library, string name)
    {
        if (Find(library, name) is not null)
            throw new InvalidOperationException($"A playlist named '{name}' already exists.");
        var playlist = new Playlist { Name = name, CreatedAt = DateTimeOffset.UtcNow };
        library.Playlists.Add(playlist);
        return playlist;
    }

    /// <summary>Adds every track matching <paramref name="query"/> that is
    /// not already in the playlist. Returns how many were added.</summary>
    public static int AddBySearch(Library library, Playlist playlist, string query)
    {
        var matches = LibraryQuery.Search(library.Tracks, query);
        var added = 0;
        foreach (var track in matches)
        {
            if (playlist.TrackIds.Contains(track.Id, StringComparer.Ordinal))
                continue;
            playlist.TrackIds.Add(track.Id);
            added++;
        }
        return added;
    }

    public static Track RemoveAt(Library library, Playlist playlist, int oneBasedIndex)
    {
        if (oneBasedIndex < 1 || oneBasedIndex > playlist.TrackIds.Count)
            throw new ArgumentOutOfRangeException(nameof(oneBasedIndex),
                $"Playlist '{playlist.Name}' has {playlist.TrackIds.Count} track(s).");
        var id = playlist.TrackIds[oneBasedIndex - 1];
        playlist.TrackIds.RemoveAt(oneBasedIndex - 1);
        return library.Tracks.FirstOrDefault(t => t.Id == id)
            ?? new Track { Id = id, Path = "(track no longer in library)", Format = "" };
    }

    public static List<Track> Resolve(Library library, Playlist playlist) =>
        playlist.TrackIds
            .Select(id => library.Tracks.FirstOrDefault(t => t.Id == id))
            .Where(t => t is not null)
            .Select(t => t!)
            .ToList();
}

/// <summary>Writes a playlist as an extended M3U file: an #EXTM3U header,
/// one #EXTINF line per track (duration in whole seconds, "Artist - Title"),
/// then the track's path.</summary>
public static class M3uExporter
{
    public static void Export(Library library, Playlist playlist, string filePath)
    {
        var tracks = PlaylistService.Resolve(library, playlist);
        using var writer = new StreamWriter(filePath);
        writer.WriteLine("#EXTM3U");
        foreach (var track in tracks)
        {
            var seconds = track.Duration is null ? -1 : (int)track.Duration.Value.TotalSeconds;
            writer.WriteLine($"#EXTINF:{seconds},{track.DisplayArtist} - {track.DisplayTitle}");
            writer.WriteLine(track.Path);
        }
    }
}
