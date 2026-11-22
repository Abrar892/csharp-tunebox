namespace TuneBox;

public sealed record LibraryStatistics
{
    public int TrackCount { get; init; }
    public long TotalBytes { get; init; }
    public TimeSpan TotalDuration { get; init; }
    public int TracksWithDuration { get; init; }
    public List<KeyValuePair<string, int>> TopArtists { get; init; } = new();
    public List<KeyValuePair<string, int>> TopGenres { get; init; } = new();
    public List<KeyValuePair<string, int>> Formats { get; init; } = new();
    public int? EarliestYear { get; init; }
    public int? LatestYear { get; init; }
    public int MissingTitleCount { get; init; }
    public int MissingArtistCount { get; init; }
}

/// <summary>Whole-library aggregates for the `stats` command.</summary>
public static class LibraryStats
{
    public static LibraryStatistics Compute(IEnumerable<Track> tracks)
    {
        var list = tracks.ToList();
        var years = list.Where(t => t.Year is not null).Select(t => t.Year!.Value).ToList();
        var withDuration = list.Where(t => t.Duration is not null).ToList();

        return new LibraryStatistics
        {
            TrackCount = list.Count,
            TotalBytes = list.Sum(t => t.FileSize),
            TotalDuration = TimeSpan.FromTicks(withDuration.Sum(t => t.Duration!.Value.Ticks)),
            TracksWithDuration = withDuration.Count,
            TopArtists = Top(list, t => t.Artist, "Unknown artist"),
            TopGenres = Top(list, t => t.Genre, "Unknown genre"),
            Formats = Top(list, t => t.Format, "unknown"),
            EarliestYear = years.Count == 0 ? null : years.Min(),
            LatestYear = years.Count == 0 ? null : years.Max(),
            MissingTitleCount = list.Count(t => t.Title is null),
            MissingArtistCount = list.Count(t => t.Artist is null),
        };
    }

    private static List<KeyValuePair<string, int>> Top(
        List<Track> tracks, Func<Track, string?> key, string fallback)
    {
        return tracks
            .GroupBy(t => string.IsNullOrWhiteSpace(key(t)) ? fallback : key(t)!)
            .Select(g => new KeyValuePair<string, int>(g.Key, g.Count()))
            .OrderByDescending(p => p.Value)
            .ThenBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToList();
    }
}

/// <summary>Finds likely copies of the same song: tracks whose normalised
/// title and artist agree but whose audio fingerprints differ — the same
/// recording re-encoded, or the same song from two releases. The scanner
/// already merges byte-identical files, so these are the ones it cannot
/// prove identical on its own.</summary>
public static class DuplicateFinder
{
    public static List<List<Track>> Find(IEnumerable<Track> tracks)
    {
        return tracks
            .Where(t => t.Title is not null && t.Artist is not null)
            .GroupBy(t => (Normalize(t.Title!), Normalize(t.Artist!)))
            .Where(g => g.Select(t => t.Id).Distinct(StringComparer.Ordinal).Count() > 1)
            .Select(g => g.OrderBy(t => t.Path, StringComparer.OrdinalIgnoreCase).ToList())
            .OrderByDescending(g => g.Count)
            .ToList();
    }

    /// <summary>Lower-case, punctuation and whitespace stripped, so
    /// "Don't Stop" and "dont stop" normalise to the same key.</summary>
    public static string Normalize(string value)
    {
        Span<char> buffer = stackalloc char[value.Length];
        var n = 0;
        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch))
                buffer[n++] = char.ToLowerInvariant(ch);
        }
        return new string(buffer[..n]);
    }
}
