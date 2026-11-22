using System.Text;

namespace TuneBox;

/// <summary>Tags read from an audio file, plus where the actual audio data
/// starts so a content fingerprint can skip the tag bytes.</summary>
public sealed record AudioTags
{
    public string? Title { get; init; }
    public string? Artist { get; init; }
    public string? Album { get; init; }
    public int? Year { get; init; }
    public int? TrackNumber { get; init; }
    public string? Genre { get; init; }

    /// <summary>Bitrate in kbps from the first MPEG audio frame, when found.</summary>
    public int? BitrateKbps { get; init; }

    /// <summary>Byte offset where audio data begins (after any ID3v2 tag).</summary>
    public long AudioStartOffset { get; init; }

    /// <summary>Audio bytes from <see cref="AudioStartOffset"/> to the end
    /// of the file, excluding a trailing ID3v1 tag.</summary>
    public long AudioByteCount { get; init; }
}

/// <summary>Hand-rolled reader for ID3v1 and ID3v2.3/2.4 tags in MP3 files,
/// plus a first-frame MPEG header read for the bitrate. No dependencies:
/// the tag layout is small enough to parse directly, which also keeps
/// malformed files from crashing the scan — a bad tag just yields no tags.</summary>
public static class Id3Parser
{
    private static readonly string[] Genres =
    {
        "Blues", "Classic Rock", "Country", "Dance", "Disco", "Funk", "Grunge",
        "Hip-Hop", "Jazz", "Metal", "New Age", "Oldies", "Other", "Pop", "R&B",
        "Rap", "Reggae", "Rock", "Techno", "Industrial", "Alternative", "Ska",
        "Death Metal", "Pranks", "Soundtrack", "Euro-Techno", "Ambient",
        "Trip-Hop", "Vocal", "Jazz+Funk", "Fusion", "Trance", "Classical",
        "Instrumental", "Acid", "House", "Game", "Sound Clip", "Gospel", "Noise",
        "AlternRock", "Bass", "Soul", "Punk", "Space", "Meditative",
        "Instrumental Pop", "Instrumental Rock", "Ethnic", "Gothic", "Darkwave",
        "Techno-Industrial", "Electronic", "Pop-Folk", "Eurodance", "Dream",
        "Southern Rock", "Comedy", "Cult", "Gangsta", "Top 40", "Christian Rap",
        "Pop/Funk", "Jungle", "Native American", "Cabaret", "New Wave",
        "Psychadelic", "Rave", "Showtunes", "Trailer", "Lo-Fi", "Tribal",
        "Acid Punk", "Acid Jazz", "Polka", "Retro", "Musical", "Rock & Roll",
        "Hard Rock",
    };

    /// <summary>Reads the tags of an MP3 file. Returns empty tags (not null)
    /// when the file has no readable tag at all.</summary>
    public static AudioTags Parse(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Parse(stream);
    }

    public static AudioTags Parse(Stream stream)
    {
        var length = stream.Length;
        var v2 = ReadId3v2(stream);
        var audioStart = v2?.TagSize ?? 0;
        var v1 = ReadId3v1(stream, length);

        // v2 wins for every field it provides; v1 fills the gaps.
        var tags = Merge(v2, v1);

        var bitrate = FindFirstFrameBitrate(stream, audioStart);
        var audioBytes = Math.Max(0, length - audioStart - (v1 is null ? 0 : 128));

        return tags with { BitrateKbps = bitrate, AudioStartOffset = audioStart, AudioByteCount = audioBytes };
    }

    // ---- ID3v2 ----------------------------------------------------------

    private sealed record V2Result(AudioTags Tags, long TagSize);

    private static V2Result? ReadId3v2(Stream stream)
    {
        stream.Seek(0, SeekOrigin.Begin);
        Span<byte> header = stackalloc byte[10];
        if (stream.Read(header) < 10)
            return null;
        if (header[0] != (byte)'I' || header[1] != (byte)'D' || header[2] != (byte)'3')
            return null;

        var major = header[3];
        if (major is not (3 or 4))
            return null; // v2.2 uses a different frame layout; not attempted

        var size = Synchsafe(header[6], header[7], header[8], header[9]);
        var tagEnd = 10L + size;
        var extendedHeader = (header[5] & 0x40) != 0;

        var title = (string?)null;
        var artist = (string?)null;
        var album = (string?)null;
        var year = (int?)null;
        var track = (int?)null;
        var genre = (string?)null;

        if (extendedHeader)
        {
            // v2.3: 4-byte big-endian size; v2.4: 4-byte synchsafe size.
            Span<byte> ext = stackalloc byte[4];
            if (stream.Read(ext) < 4)
                return null;
            var extSize = major == 4
                ? Synchsafe(ext[0], ext[1], ext[2], ext[3])
                : (ext[0] << 24) | (ext[1] << 16) | (ext[2] << 8) | ext[3];
            stream.Seek(extSize, SeekOrigin.Current);
        }

        Span<byte> frameHeader = stackalloc byte[10];
        while (stream.Position + 10 <= tagEnd)
        {
            if (stream.Read(frameHeader) < 10)
                break;
            if (frameHeader[0] == 0)
                break; // padding

            var frameId = Encoding.ASCII.GetString(frameHeader[..4]);
            var frameSize = major == 4
                ? Synchsafe(frameHeader[4], frameHeader[5], frameHeader[6], frameHeader[7])
                : (frameHeader[4] << 24) | (frameHeader[5] << 16) | (frameHeader[6] << 8) | frameHeader[7];
            if (frameSize <= 0 || stream.Position + frameSize > tagEnd)
                break;

            var body = new byte[frameSize];
            if (stream.Read(body) < frameSize)
                break;

            switch (frameId)
            {
                case "TIT2": title = DecodeText(body); break;
                case "TPE1": artist = DecodeText(body); break;
                case "TALB": album = DecodeText(body); break;
                case "TYER":
                case "TDRC": year = ParseYear(DecodeText(body)); break;
                case "TRCK": track = ParseTrackNumber(DecodeText(body)); break;
                case "TCON": genre = ParseGenre(DecodeText(body)); break;
            }
        }

        var tags = new AudioTags
        {
            Title = NullIfBlank(title),
            Artist = NullIfBlank(artist),
            Album = NullIfBlank(album),
            Year = year,
            TrackNumber = track,
            Genre = NullIfBlank(genre),
        };
        return new V2Result(tags, tagEnd);
    }

    private static int Synchsafe(byte b0, byte b1, byte b2, byte b3) =>
        (b0 << 21) | (b1 << 14) | (b2 << 7) | b3;

    private static string? DecodeText(byte[] body)
    {
        if (body.Length == 0)
            return null;
        var encoding = body[0] switch
        {
            0 => Encoding.Latin1,
            1 => Encoding.Unicode, // UTF-16 with BOM; .NET sniffs the BOM
            2 => Encoding.BigEndianUnicode,
            3 => Encoding.UTF8,
            _ => Encoding.Latin1,
        };
        var text = encoding.GetString(body, 1, body.Length - 1);
        // Encoding 1 leaves a BOM char when the payload starts with one.
        return text.Trim('\0', ' ', '\uFEFF');
    }

    private static int? ParseYear(string? text)
    {
        if (text is null || text.Length < 4)
            return null;
        return int.TryParse(text[..4], out var year) ? year : null;
    }

    private static int? ParseTrackNumber(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var slash = text.IndexOf('/');
        var head = slash >= 0 ? text[..slash] : text;
        return int.TryParse(head.Trim(), out var n) ? n : null;
    }

    private static string? ParseGenre(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        // Numeric genre in parentheses, e.g. "(17)" or "(17)Rock".
        if (text[0] == '(')
        {
            var close = text.IndexOf(')');
            if (close > 1 && int.TryParse(text[1..close], out var index) && index < Genres.Length)
                return Genres[index];
        }
        return int.TryParse(text, out var numeric) && numeric < Genres.Length
            ? Genres[numeric]
            : text;
    }

    // ---- ID3v1 ----------------------------------------------------------

    private static AudioTags? ReadId3v1(Stream stream, long length)
    {
        if (length < 128)
            return null;
        stream.Seek(length - 128, SeekOrigin.Begin);
        var tag = new byte[128];
        if (stream.Read(tag) < 128)
            return null;
        if (tag[0] != (byte)'T' || tag[1] != (byte)'A' || tag[2] != (byte)'G')
            return null;

        string Field(int offset, int count) =>
            Encoding.Latin1.GetString(tag, offset, count).Trim('\0', ' ');

        int? track = null;
        if (tag[125] == 0 && tag[126] != 0)
            track = tag[126];

        int? year = null;
        var yearText = Field(93, 4);
        if (int.TryParse(yearText, out var y))
            year = y;

        string? genre = null;
        if (tag[127] < Genres.Length)
            genre = Genres[tag[127]];

        return new AudioTags
        {
            Title = NullIfBlank(Field(3, 30)),
            Artist = NullIfBlank(Field(33, 30)),
            Album = NullIfBlank(Field(63, 30)),
            Year = year,
            TrackNumber = track,
            Genre = genre,
        };
    }

    private static AudioTags Merge(V2Result? v2, AudioTags? v1)
    {
        if (v2 is null)
            return v1 ?? new AudioTags();
        if (v1 is null)
            return v2.Tags;
        var a = v2.Tags;
        var b = v1;
        return a with
        {
            Title = a.Title ?? b.Title,
            Artist = a.Artist ?? b.Artist,
            Album = a.Album ?? b.Album,
            Year = a.Year ?? b.Year,
            TrackNumber = a.TrackNumber ?? b.TrackNumber,
            Genre = a.Genre ?? b.Genre,
        };
    }

    // ---- MPEG frame header ----------------------------------------------

    // Bitrate tables [mpegVersion: 1, 2/2.5][layer: I, II, III][index 1..14], kbps.
    private static readonly int[][][] Bitrates =
    {
        // MPEG1
        new[]
        {
            new[] { 32, 64, 96, 128, 160, 192, 224, 256, 288, 320, 352, 384, 416, 448 },
            new[] { 32, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 384 },
            new[] { 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320 },
        },
        // MPEG2 / MPEG2.5
        new[]
        {
            new[] { 32, 64, 96, 128, 160, 192, 224, 256, 288, 320, 352, 384, 416, 448 },
            new[] { 32, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 384 },
            new[] { 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160 },
        },
    };

    /// <summary>Scans forward from <paramref name="offset"/> for the first
    /// MPEG audio frame header and returns its bitrate, or null. Limited to
    /// the first 64 KiB so junk-prefixed files do not stall the scan.</summary>
    private static int? FindFirstFrameBitrate(Stream stream, long offset)
    {
        const int window = 64 * 1024;
        var available = (int)Math.Min(window, stream.Length - offset);
        if (available < 4)
            return null;

        stream.Seek(offset, SeekOrigin.Begin);
        var buffer = new byte[available];
        if (stream.Read(buffer) < 4)
            return null;

        for (var i = 0; i + 4 <= buffer.Length; i++)
        {
            if (buffer[i] != 0xFF || (buffer[i + 1] & 0xE0) != 0xE0)
                continue;
            var versionBits = (buffer[i + 1] >> 3) & 0x3;
            var layerBits = (buffer[i + 1] >> 1) & 0x3;
            var bitrateIndex = (buffer[i + 2] >> 4) & 0xF;
            if (versionBits == 1 || layerBits == 0 || bitrateIndex is 0 or 15)
                continue;

            var versionTable = versionBits == 3 ? 0 : 1;
            var layerTable = layerBits switch { 3 => 0, 2 => 1, 1 => 2, _ => -1 };
            if (layerTable < 0)
                continue;
            return Bitrates[versionTable][layerTable][bitrateIndex - 1];
        }
        return null;
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
