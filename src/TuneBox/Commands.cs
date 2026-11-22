namespace TuneBox;

internal static class ScanCommand
{
    public static int Run(LibraryStore store, string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: tunebox scan <dir> [--recursive]");
            return 2;
        }

        var directory = args[0];
        var recursive = args.Contains("--recursive") || args.Contains("-r");
        if (!Directory.Exists(directory))
        {
            Console.Error.WriteLine($"tunebox: directory not found: {directory}");
            return 2;
        }

        var library = store.Load();
        var result = AudioScanner.ScanInto(library, directory, recursive);
        store.Save(library);

        Console.WriteLine($"Scanned {directory}");
        Console.WriteLine($"  added:     {result.Added}");
        Console.WriteLine($"  updated:   {result.Updated} (same recording, new path)");
        Console.WriteLine($"  unchanged: {result.Unchanged}");
        Console.WriteLine($"  skipped:   {result.Skipped}");
        Console.WriteLine($"Library now holds {library.Tracks.Count} tracks.");
        foreach (var error in result.Errors)
            Console.Error.WriteLine($"  warning: {error}");
        return 0;
    }
}

internal static class ListCommand
{
    public static int Run(LibraryStore store, string[] args)
    {
        string? artist = null;
        string? album = null;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--artist" && i + 1 < args.Length) artist = args[++i];
            else if (args[i] == "--album" && i + 1 < args.Length) album = args[++i];
        }

        var tracks = LibraryQuery.Filter(store.Load().Tracks, artist, album);
        foreach (var track in tracks)
        {
            var meta = new List<string>();
            if (track.Album is not null) meta.Add(track.Album);
            if (track.Year is not null) meta.Add(track.Year.Value.ToString());
            var suffix = meta.Count > 0 ? $" ({string.Join(", ", meta)})" : "";
            var duration = track.Duration is null ? "" : $" [{track.Duration.Value:mm\\:ss}]";
            Console.WriteLine($"{track.DisplayArtist} — {track.DisplayTitle}{suffix}{duration}");
        }
        Console.WriteLine($"{tracks.Count} track(s).");
        return 0;
    }
}

internal static class InfoCommand
{
    public static int Run(LibraryStore store, string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: tunebox info <file>");
            return 2;
        }

        var path = args[0];
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"tunebox: file not found: {path}");
            return 2;
        }

        var track = AudioScanner.ReadTrack(path);
        if (track is null)
        {
            Console.Error.WriteLine("tunebox: could not read that file as audio.");
            return 1;
        }

        Console.WriteLine($"File:     {track.Path}");
        Console.WriteLine($"Format:   {track.Format} ({track.FileSize:N0} bytes)");
        Console.WriteLine($"Title:    {track.Title ?? "(none)"}");
        Console.WriteLine($"Artist:   {track.Artist ?? "(none)"}");
        Console.WriteLine($"Album:    {track.Album ?? "(none)"}");
        Console.WriteLine($"Year:     {track.Year?.ToString() ?? "(none)"}");
        Console.WriteLine($"Track:    {track.TrackNumber?.ToString() ?? "(none)"}");
        Console.WriteLine($"Genre:    {track.Genre ?? "(none)"}");
        Console.WriteLine($"Duration: {(track.Duration is null ? "(unknown)" : track.Duration.Value.ToString(@"mm\:ss"))}");
        Console.WriteLine($"SHA-256:  {track.Id}");
        return 0;
    }
}

internal static class SearchCommand
{
    public static int Run(LibraryStore store, string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: tunebox search <query>");
            return 2;
        }

        var query = string.Join(' ', args);
        var results = LibraryQuery.Search(store.Load().Tracks, query);
        foreach (var track in results)
        {
            var suffix = track.Album is null ? "" : $" ({track.Album})";
            Console.WriteLine($"{track.DisplayArtist} — {track.DisplayTitle}{suffix}");
        }
        Console.WriteLine($"{results.Count} result(s) for \"{query}\".");
        return 0;
    }
}

internal static class StatsCommand
{
    public static int Run(LibraryStore store, string[] args)
    {
        var stats = LibraryStats.Compute(store.Load().Tracks);

        Console.WriteLine($"Tracks:   {stats.TrackCount}");
        Console.WriteLine($"Size:     {FormatBytes(stats.TotalBytes)}");
        Console.WriteLine(stats.TracksWithDuration > 0
            ? $"Duration: {FormatDuration(stats.TotalDuration)} (known for {stats.TracksWithDuration} track(s))"
            : "Duration: unknown (no track exposed a bitrate)");
        if (stats.EarliestYear is not null)
            Console.WriteLine($"Years:    {stats.EarliestYear}–{stats.LatestYear}");
        Console.WriteLine($"Untagged: {stats.MissingTitleCount} without title, {stats.MissingArtistCount} without artist");

        PrintTop("Top artists", stats.TopArtists);
        PrintTop("Top genres", stats.TopGenres);
        PrintTop("Formats", stats.Formats);
        return 0;
    }

    private static void PrintTop(string heading, List<KeyValuePair<string, int>> entries)
    {
        if (entries.Count == 0)
            return;
        Console.WriteLine($"{heading}:");
        foreach (var (name, count) in entries)
            Console.WriteLine($"  {name} ({count})");
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):F2} GiB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):F2} MiB",
        >= 1L << 10 => $"{bytes / (double)(1L << 10):F1} KiB",
        _ => $"{bytes} B",
    };

    private static string FormatDuration(TimeSpan span) =>
        span.TotalHours >= 1
            ? $"{(int)span.TotalHours}h {span.Minutes}m"
            : $"{span.Minutes}m {span.Seconds}s";
}

internal static class DuplicatesCommand
{
    public static int Run(LibraryStore store, string[] args)
    {
        var groups = DuplicateFinder.Find(store.Load().Tracks);
        if (groups.Count == 0)
        {
            Console.WriteLine("No likely duplicates found.");
            return 0;
        }

        foreach (var group in groups)
        {
            var first = group[0];
            Console.WriteLine($"{first.DisplayArtist} — {first.DisplayTitle} ({group.Count} copies):");
            foreach (var track in group)
                Console.WriteLine($"  {track.Path} ({track.FileSize:N0} bytes)");
        }
        Console.WriteLine($"{groups.Count} duplicate group(s), {groups.Sum(g => g.Count - 1)} extra copie(s).");
        return 0;
    }
}
