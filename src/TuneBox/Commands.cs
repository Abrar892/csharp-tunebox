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
