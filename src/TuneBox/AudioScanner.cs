using System.Security.Cryptography;

namespace TuneBox;

public sealed record ScanResult
{
    public int Added { get; init; }
    public int Updated { get; init; }
    public int Unchanged { get; init; }
    public int Skipped { get; init; }
    public List<string> Errors { get; init; } = new();
}

/// <summary>Walks a directory for audio files, reads their tags, and merges
/// them into the library. Identity is the SHA-256 of the audio bytes (tag
/// bytes excluded), so re-tagging a file never creates a second entry and
/// the same recording found under two paths is one track, not two.</summary>
public static class AudioScanner
{
    public static readonly string[] SupportedExtensions =
        { ".mp3", ".flac", ".ogg", ".oga", ".wav", ".m4a", ".aac" };

    /// <summary>Scans <paramref name="directory"/> and merges the results
    /// into <paramref name="library"/>. The caller saves the library.</summary>
    public static ScanResult ScanInto(Library library, string directory, bool recursive)
    {
        var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var files = Directory.EnumerateFiles(directory, "*", option)
            .Where(f => SupportedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var byId = library.Tracks.ToDictionary(t => t.Id, StringComparer.Ordinal);
        var added = 0;
        var updated = 0;
        var unchanged = 0;
        var skipped = 0;
        var errors = new List<string>();

        foreach (var file in files)
        {
            try
            {
                var track = ReadTrack(file);
                if (track is null)
                {
                    skipped++;
                    continue;
                }

                if (byId.TryGetValue(track.Id, out var existing))
                {
                    if (string.Equals(existing.Path, track.Path, StringComparison.Ordinal))
                    {
                        unchanged++;
                    }
                    else
                    {
                        // Same recording, new location: keep the original
                        // AddedAt, take the fresh path and tags.
                        library.Tracks.Remove(existing);
                        library.Tracks.Add(existing with
                        {
                            Path = track.Path,
                            Title = track.Title ?? existing.Title,
                            Artist = track.Artist ?? existing.Artist,
                            Album = track.Album ?? existing.Album,
                            Year = track.Year ?? existing.Year,
                            TrackNumber = track.TrackNumber ?? existing.TrackNumber,
                            Genre = track.Genre ?? existing.Genre,
                            FileSize = track.FileSize,
                            Duration = track.Duration ?? existing.Duration,
                        });
                        byId[track.Id] = library.Tracks[^1];
                        updated++;
                    }
                }
                else
                {
                    library.Tracks.Add(track);
                    byId.Add(track.Id, track);
                    added++;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                skipped++;
                errors.Add($"{file}: {ex.Message}");
            }
        }

        return new ScanResult
        {
            Added = added,
            Updated = updated,
            Unchanged = unchanged,
            Skipped = skipped,
            Errors = errors,
        };
    }

    /// <summary>Reads one file into a <see cref="Track"/>, or null when the
    /// file cannot be read as audio at all.</summary>
    public static Track? ReadTrack(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length == 0)
            return null;

        var format = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        var tags = format == "mp3" ? Id3Parser.Parse(path) : FallbackTags(path, format);
        var fingerprint = Fingerprint(path, tags);

        TimeSpan? duration = null;
        if (tags.BitrateKbps is > 0 && tags.AudioByteCount > 0)
            duration = TimeSpan.FromSeconds(tags.AudioByteCount * 8.0 / (tags.BitrateKbps.Value * 1000.0));

        return new Track
        {
            Id = fingerprint,
            Path = Path.GetFullPath(path),
            Title = tags.Title,
            Artist = tags.Artist,
            Album = tags.Album,
            Year = tags.Year,
            TrackNumber = tags.TrackNumber,
            Genre = tags.Genre,
            FileSize = info.Length,
            Duration = duration,
            Format = format,
            AddedAt = DateTimeOffset.UtcNow,
        };
    }

    private static AudioTags FallbackTags(string path, string format)
    {
        // Only MP3 gets a real tag reader; other formats fall back to the
        // file name, with the whole file as the audio range.
        var fromName = FilenameParser.Parse(path);
        return fromName with
        {
            AudioStartOffset = 0,
            AudioByteCount = new FileInfo(path).Length,
        };
    }

    /// <summary>SHA-256 of the audio bytes only, streamed in 64 KiB chunks
    /// so large files never sit fully in memory.</summary>
    private static string Fingerprint(string path, AudioTags tags)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var sha = SHA256.Create();
        stream.Seek(tags.AudioStartOffset, SeekOrigin.Begin);
        var remaining = tags.AudioByteCount;
        var buffer = new byte[64 * 1024];
        while (remaining > 0)
        {
            var toRead = (int)Math.Min(buffer.Length, remaining);
            var read = stream.Read(buffer, 0, toRead);
            if (read == 0)
                break;
            sha.TransformBlock(buffer, 0, read, null, 0);
            remaining -= read;
        }
        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexString(sha.Hash!).ToLowerInvariant();
    }
}
