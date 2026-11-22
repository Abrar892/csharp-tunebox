namespace TuneBox;

/// <summary>A single audio file in the library. <see cref="Id"/> is the
/// content fingerprint, so the same recording re-scanned from a new path is
/// recognised as the same track instead of a duplicate entry.</summary>
public sealed record Track
{
    public required string Id { get; init; }
    public required string Path { get; init; }
    public string? Title { get; init; }
    public string? Artist { get; init; }
    public string? Album { get; init; }
    public int? Year { get; init; }
    public int? TrackNumber { get; init; }
    public string? Genre { get; init; }

    /// <summary>File size in bytes.</summary>
    public long FileSize { get; init; }

    /// <summary>Estimated duration, derived from the first audio frame's
    /// bitrate when the file exposes one; null when it cannot be estimated.</summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>Lower-case extension without the dot, e.g. "mp3".</summary>
    public required string Format { get; init; }

    public DateTimeOffset AddedAt { get; init; }

    public string DisplayTitle => Title ?? System.IO.Path.GetFileNameWithoutExtension(Path);
    public string DisplayArtist => Artist ?? "Unknown artist";
}

/// <summary>An ordered list of track ids.</summary>
public sealed record Playlist
{
    public required string Name { get; init; }
    public List<string> TrackIds { get; init; } = new();
    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>The whole library document, persisted as one JSON file.</summary>
public sealed class Library
{
    public int Version { get; set; } = 1;
    public List<Track> Tracks { get; set; } = new();
    public List<Playlist> Playlists { get; set; } = new();
}
