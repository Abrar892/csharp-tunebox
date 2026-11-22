namespace TuneBox;

/// <summary>TuneBox — a CLI music library manager.
/// Scans folders for audio files, reads their tags, and keeps an indexed
/// library you can search, analyse and turn into playlists.</summary>
internal static class Program
{
    private const string Version = "1.0.0";

    internal static int Main(string[] args)
    {
        var (libraryPath, rest) = CliOptions.ExtractLibraryPath(args);

        if (rest.Length == 0 || rest[0] is "-h" or "--help" or "help")
        {
            PrintHelp();
            return 0;
        }

        if (rest[0] is "-v" or "--version" or "version")
        {
            Console.WriteLine($"tunebox {Version}");
            return 0;
        }

        var store = new LibraryStore(libraryPath);
        return rest[0] switch
        {
            "init" => Init(store),
            "scan" => ScanCommand.Run(store, rest[1..]),
            "list" => ListCommand.Run(store, rest[1..]),
            "info" => InfoCommand.Run(store, rest[1..]),
            _ => UnknownCommand(rest[0]),
        };
    }

    private static int Init(LibraryStore store)
    {
        if (File.Exists(store.Path))
        {
            Console.WriteLine($"Library already exists at {store.Path}");
            return 0;
        }

        store.Save(new Library());
        Console.WriteLine($"Created empty library at {store.Path}");
        return 0;
    }

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"tunebox: unknown command '{command}'");
        Console.Error.WriteLine("Run 'tunebox help' for usage.");
        return 2;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            tunebox — CLI music library manager

            Usage:
              tunebox [--library <path>] <command> [args]

            Commands:
              init                          Create an empty library file
              scan <dir> [--recursive]      Index audio files in a directory
              list [--artist X] [--album X] List tracks, optionally filtered
              search <query>                Search title, artist, album, genre
              stats                         Library totals and top artists
              duplicates                    Find tracks sharing one recording
              playlist create <name>
              playlist add <name> <query>
              playlist remove <name> <index>
              playlist list
              playlist show <name>
              playlist export <name> <file.m3u>
              info <file>                   Show tags for one audio file
              version                       Print the version

            The library defaults to ~/.tunebox/library.json
            (override with --library or TUNEBOX_LIBRARY).
            """);
    }
}

/// <summary>Parses the --library global option out of the argument list
/// before the command dispatcher sees it.</summary>
internal static class CliOptions
{
    public static (string LibraryPath, string[] Args) ExtractLibraryPath(string[] args)
    {
        var rest = new List<string>();
        var libraryPath = LibraryStore.DefaultPath();

        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--library" && i + 1 < args.Length)
            {
                libraryPath = args[++i];
            }
            else if (args[i].StartsWith("--library=", StringComparison.Ordinal))
            {
                libraryPath = args[i]["--library=".Length..];
            }
            else
            {
                rest.Add(args[i]);
            }
        }

        return (libraryPath, rest.ToArray());
    }
}
