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

internal static class PlaylistCommand
{
    public static int Run(LibraryStore store, string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("""
                Usage:
                  tunebox playlist create <name>
                  tunebox playlist add <name> <query>
                  tunebox playlist remove <name> <index>
                  tunebox playlist list
                  tunebox playlist show <name>
                  tunebox playlist export <name> <file.m3u>
                """);
            return 2;
        }

        var library = store.Load();
        try
        {
            return args[0] switch
            {
                "create" => Create(library, store, args),
                "add" => Add(library, store, args),
                "remove" => Remove(library, store, args),
                "list" => List(library),
                "show" => Show(library, args),
                "export" => Export(library, args),
                _ => Unknown(args[0]),
            };
        }
        catch (InvalidOperationException ex)
        {
            Console.Error.WriteLine($"tunebox: {ex.Message}");
            return 1;
        }
        catch (ArgumentOutOfRangeException ex)
        {
            Console.Error.WriteLine($"tunebox: {ex.Message}");
            return 1;
        }
    }

    private static int Create(Library library, LibraryStore store, string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: tunebox playlist create <name>");
            return 2;
        }
        var name = string.Join(' ', args[1..]);
        PlaylistService.Create(library, name);
        store.Save(library);
        Console.WriteLine($"Created playlist '{name}'.");
        return 0;
    }

    private static int Add(Library library, LibraryStore store, string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("Usage: tunebox playlist add <name> <query>");
            return 2;
        }
        var playlist = Require(library, args[1]);
        if (playlist is null) return 1;
        var query = string.Join(' ', args[2..]);
        var added = PlaylistService.AddBySearch(library, playlist, query);
        store.Save(library);
        Console.WriteLine(added == 0
            ? $"No new tracks matching \"{query}\" for playlist '{playlist.Name}'."
            : $"Added {added} track(s) to '{playlist.Name}'.");
        return 0;
    }

    private static int Remove(Library library, LibraryStore store, string[] args)
    {
        if (args.Length < 3 || !int.TryParse(args[2], out var index))
        {
            Console.Error.WriteLine("Usage: tunebox playlist remove <name> <index>");
            return 2;
        }
        var playlist = Require(library, args[1]);
        if (playlist is null) return 1;
        var removed = PlaylistService.RemoveAt(library, playlist, index);
        store.Save(library);
        Console.WriteLine($"Removed '{removed.DisplayArtist} — {removed.DisplayTitle}' from '{playlist.Name}'.");
        return 0;
    }

    private static int List(Library library)
    {
        if (library.Playlists.Count == 0)
        {
            Console.WriteLine("No playlists yet. Create one with 'tunebox playlist create <name>'.");
            return 0;
        }
        foreach (var playlist in library.Playlists.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
            Console.WriteLine($"{playlist.Name} ({playlist.TrackIds.Count} track(s))");
        return 0;
    }

    private static int Show(Library library, string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: tunebox playlist show <name>");
            return 2;
        }
        var playlist = Require(library, args[1]);
        if (playlist is null) return 1;
        var tracks = PlaylistService.Resolve(library, playlist);
        Console.WriteLine($"Playlist '{playlist.Name}':");
        for (var i = 0; i < tracks.Count; i++)
            Console.WriteLine($"  {i + 1}. {tracks[i].DisplayArtist} — {tracks[i].DisplayTitle}");
        if (tracks.Count < playlist.TrackIds.Count)
            Console.WriteLine($"  ({playlist.TrackIds.Count - tracks.Count} track(s) no longer in the library)");
        return 0;
    }

    private static int Export(Library library, string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("Usage: tunebox playlist export <name> <file.m3u>");
            return 2;
        }
        var playlist = Require(library, args[1]);
        if (playlist is null) return 1;
        M3uExporter.Export(library, playlist, args[2]);
        Console.WriteLine($"Exported '{playlist.Name}' ({playlist.TrackIds.Count} track(s)) to {args[2]}.");
        return 0;
    }

    private static Playlist? Require(Library library, string name)
    {
        var playlist = PlaylistService.Find(library, name);
        if (playlist is null)
            Console.Error.WriteLine($"tunebox: no playlist named '{name}'.");
        return playlist;
    }

    private static int Unknown(string subcommand)
    {
        Console.Error.WriteLine($"tunebox: unknown playlist command '{subcommand}'");
        return 2;
    }
}
