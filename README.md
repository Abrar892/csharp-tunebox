# TuneBox — CLI Music Library Manager

TuneBox scans folders of audio files, reads their tags, and keeps an indexed
library you can search, analyse, and turn into playlists. It is a single
self-contained .NET 8 console app with zero runtime dependencies.

## Why this exists

Music players index your library for you, but they hide the index. TuneBox
keeps the index as a plain JSON file you own: easy to back up, easy to grep,
easy to script against. It is also a good excuse to parse a real binary
format by hand — ID3 tags are small enough to read directly, with no library
in the way.

## Features

- **Scan** a directory (optionally recursive) for MP3, FLAC, OGG, WAV, M4A
  and AAC files, and index them into `~/.tunebox/library.json`.
- **Hand-rolled ID3v1 and ID3v2.3/2.4 parsing** for MP3s — text frames,
  numeric genres (`(17)` → Rock), track numbers (`3/12`), and a bitrate
  read from the first MPEG frame for duration estimates. Other formats
  fall back to `Artist - Title` file-name parsing.
- **Content fingerprinting**: a track's identity is the SHA-256 of its
  audio bytes (tag bytes excluded). Re-tagging a file never creates a
  second entry, and the same recording found under a new path is updated,
  not duplicated.
- **Search** across title, artist, album and genre, ranked by field
  relevance, with every query word required to match somewhere.
- **Stats**: totals, size, playtime, top artists/genres/formats, year
  range, and how many tracks are missing tags.
- **Duplicate detection**: groups tracks whose normalised title and
  artist agree but whose fingerprints differ — the same song re-encoded,
  or from two different releases.
- **Playlists** with M3U export (`#EXTM3U` / `#EXTINF`), so any player
  can open them.

## Design decisions

1. **One JSON document, atomic writes.** The whole library is a single
   JSON file, saved via write-to-temp-then-move. A crash mid-save can
   never leave a half-written library, and the file stays human-readable.
2. **Fingerprint over audio bytes, not file bytes.** Tag edits change file
   bytes but not the recording. Hashing only the audio range makes
   identity stable across re-tags.
3. **Zero dependencies.** ID3 parsing, the JSON store, and the CLI are all
   hand-written. There is nothing to keep up to date and nothing that can
   break the build from the outside.
4. **Tags are best-effort, never fatal.** A malformed tag yields empty
   fields for that file, not a failed scan — one bad file should not stop
   a thousand good ones from being indexed.

## Usage

```bash
dotnet run --project src/TuneBox -- --library ~/music-library.json scan ~/Music --recursive
dotnet run --project src/TuneBox -- --library ~/music-library.json list --artist "Miles Davis"
dotnet run --project src/TuneBox -- --library ~/music-library.json search "blue monday"
dotnet run --project src/TuneBox -- --library ~/music-library.json stats
dotnet run --project src/TuneBox -- --library ~/music-library.json duplicates
dotnet run --project src/TuneBox -- --library ~/music-library.json playlist create "Road Trip"
dotnet run --project src/TuneBox -- --library ~/music-library.json playlist add "Road Trip" "Miles Davis"
dotnet run --project src/TuneBox -- --library ~/music-library.json playlist export "Road Trip" road-trip.m3u
dotnet run --project src/TuneBox -- info ~/Music/song.mp3
```

Without `--library`, the library lives at `~/.tunebox/library.json`
(override with the `TUNEBOX_LIBRARY` environment variable). Run
`tunebox help` for the full command list.

## Tests

```bash
dotnet test
```

36 xUnit tests cover the ID3 parser (v1, v2.3, numeric genres, malformed
tags), the scanner (idempotent re-scans, moved files, re-tagged files,
recursive walks), search ranking, stats, duplicate detection, playlists
and M3U export. The tests build their MP3 fixtures in memory, so they
need no real music files.

## Limitations

- Tag reading is implemented for MP3 only. Other formats are indexed from
  their file names; their tags are not read.
- Duration is estimated from the first frame's bitrate, so variable-bitrate
  files report an approximation.
- ID3v2.2 tags and unsynchronised frames are not parsed; those files fall
  back to their ID3v1 tag or file name.
- Duplicate detection is heuristic: two different songs that happen to
  share a normalised title and artist will be grouped together.

## License

MIT — see [LICENSE](LICENSE).
