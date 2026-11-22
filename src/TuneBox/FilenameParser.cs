namespace TuneBox;

/// <summary>Best-effort tags from a file name, for files whose format has no
/// tag reader (FLAC, OGG, WAV, M4A) or whose MP3 carries no ID3 tag.
/// Understands "Artist - Title.mp3" and "01 - Artist - Title.mp3".</summary>
public static class FilenameParser
{
    public static AudioTags Parse(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        var parts = name.Split(" - ", StringSplitOptions.TrimEntries);

        if (parts.Length == 2)
        {
            return new AudioTags { Artist = Blank(parts[0]), Title = Blank(parts[1]) };
        }

        if (parts.Length == 3 && int.TryParse(parts[0], out var track))
        {
            return new AudioTags
            {
                TrackNumber = track,
                Artist = Blank(parts[1]),
                Title = Blank(parts[2]),
            };
        }

        return new AudioTags { Title = Blank(name) };
    }

    private static string? Blank(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
