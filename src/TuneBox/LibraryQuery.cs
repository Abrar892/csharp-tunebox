namespace TuneBox;

/// <summary>Read-only queries over the library's tracks. Matching is
/// case-insensitive and culture-independent so results do not depend on
/// the machine the library was built on.</summary>
public static class LibraryQuery
{
    /// <summary>Tracks sorted by artist, then album, then track number,
    /// optionally filtered by artist and/or album substring.</summary>
    public static List<Track> Filter(
        IEnumerable<Track> tracks, string? artist, string? album)
    {
        var query = tracks.AsEnumerable();
        if (artist is not null)
            query = query.Where(t => Contains(t.Artist, artist));
        if (album is not null)
            query = query.Where(t => Contains(t.Album, album));

        return query
            .OrderBy(t => t.Artist ?? "", StringComparer.OrdinalIgnoreCase)
            .ThenBy(t => t.Album ?? "", StringComparer.OrdinalIgnoreCase)
            .ThenBy(t => t.TrackNumber ?? int.MaxValue)
            .ThenBy(t => t.DisplayTitle, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Tracks whose title, artist, album or genre contains every
    /// word of the query, ranked by how many fields matched.</summary>
    public static List<Track> Search(IEnumerable<Track> tracks, string query)
    {
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0)
            return new List<Track>();

        return tracks
            .Select(t => (Track: t, Score: Score(t, words)))
            .Where(x => x.Score > 0 && words.All(w => MatchesAnyField(x.Track, w)))
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Track.DisplayArtist, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Track.DisplayTitle, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Track)
            .ToList();
    }

    private static int Score(Track track, string[] words)
    {
        var score = 0;
        foreach (var word in words)
        {
            if (Contains(track.Title, word)) score += 3;
            if (Contains(track.Artist, word)) score += 3;
            if (Contains(track.Album, word)) score += 2;
            if (Contains(track.Genre, word)) score += 1;
        }
        return score;
    }

    private static bool MatchesAnyField(Track track, string word) =>
        Contains(track.Title, word) || Contains(track.Artist, word)
        || Contains(track.Album, word) || Contains(track.Genre, word);

    private static bool Contains(string? field, string word) =>
        field is not null && field.Contains(word, StringComparison.OrdinalIgnoreCase);
}
