using System.Text;

namespace TuneBox.Tests;

/// <summary>Builds tiny in-memory MP3 files with hand-crafted ID3 tags, so
/// the parser and scanner tests never depend on real music files.</summary>
internal static class Mp3Fixture
{
    /// <summary>An MP3 with an ID3v2.3 tag. <paramref name="audioBytes"/> is
    /// the fake audio payload that follows the tag.</summary>
    public static byte[] WithV2(
        string? title = null, string? artist = null, string? album = null,
        string? year = null, string? track = null, string? genre = null,
        int audioBytes = 512)
    {
        var frames = new List<byte>();
        AddText(frames, "TIT2", title);
        AddText(frames, "TPE1", artist);
        AddText(frames, "TALB", album);
        AddText(frames, "TYER", year);
        AddText(frames, "TRCK", track);
        AddText(frames, "TCON", genre);

        var body = frames.ToArray();
        var header = new byte[10];
        header[0] = (byte)'I'; header[1] = (byte)'D'; header[2] = (byte)'3';
        header[3] = 3; // v2.3
        var size = body.Length;
        header[6] = (byte)((size >> 21) & 0x7F);
        header[7] = (byte)((size >> 14) & 0x7F);
        header[8] = (byte)((size >> 7) & 0x7F);
        header[9] = (byte)(size & 0x7F);

        // Fake MPEG1 Layer III frame header: sync + 128 kbps @ 44.1 kHz.
        var audio = new byte[audioBytes];
        audio[0] = 0xFF; audio[1] = 0xFB; audio[2] = 0x90; audio[3] = 0x00;
        return header.Concat(body).Concat(audio).ToArray();
    }

    /// <summary>An MP3 with only an ID3v1 tag (128-byte trailer).</summary>
    public static byte[] WithV1(
        string? title = null, string? artist = null, string? album = null,
        string? year = null, int track = 0, byte genre = 255)
    {
        var audio = new byte[300];
        audio[0] = 0xFF; audio[1] = 0xFB; audio[2] = 0x90; audio[3] = 0x00;

        var tag = new byte[128];
        tag[0] = (byte)'T'; tag[1] = (byte)'A'; tag[2] = (byte)'G';
        WriteField(tag, 3, 30, title);
        WriteField(tag, 33, 30, artist);
        WriteField(tag, 63, 30, album);
        WriteField(tag, 93, 4, year);
        WriteField(tag, 97, 28, "test comment");
        tag[125] = 0;
        tag[126] = (byte)track;
        tag[127] = genre;
        return audio.Concat(tag).ToArray();
    }

    private static void AddText(List<byte> frames, string id, string? text)
    {
        if (text is null)
            return;
        var payload = new byte[] { 3 }.Concat(Encoding.UTF8.GetBytes(text)).ToArray();
        frames.AddRange(Encoding.ASCII.GetBytes(id));
        frames.AddRange(new[]
        {
            (byte)((payload.Length >> 24) & 0xFF),
            (byte)((payload.Length >> 16) & 0xFF),
            (byte)((payload.Length >> 8) & 0xFF),
            (byte)(payload.Length & 0xFF),
            (byte)0, (byte)0,
        });
        frames.AddRange(payload);
    }

    private static void WriteField(byte[] tag, int offset, int length, string? value)
    {
        if (value is null)
            return;
        var bytes = Encoding.Latin1.GetBytes(value);
        Array.Copy(bytes, 0, tag, offset, Math.Min(bytes.Length, length));
    }
}
