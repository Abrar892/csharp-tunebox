using System.Text.Json;

namespace TuneBox;

/// <summary>Loads and saves the library JSON document. Saves are atomic:
/// the new document is written to a sibling temp file and moved over the
/// old one, so a crash mid-write never leaves a half-written library.</summary>
public sealed class LibraryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public string Path { get; }

    public LibraryStore(string path) => Path = path;

    public Library Load()
    {
        if (!File.Exists(Path))
            return new Library();

        var json = File.ReadAllText(Path);
        return JsonSerializer.Deserialize<Library>(json, JsonOptions)
            ?? new Library();
    }

    public void Save(Library library)
    {
        var directory = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var tempPath = Path + ".tmp-" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            File.WriteAllText(tempPath, JsonSerializer.Serialize(library, JsonOptions));
            File.Move(tempPath, Path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    /// <summary>Resolves the library location: --library flag, then the
    /// TUNEBOX_LIBRARY environment variable, then ~/.tunebox/library.json.</summary>
    public static string DefaultPath()
    {
        var fromEnv = Environment.GetEnvironmentVariable("TUNEBOX_LIBRARY");
        if (!string.IsNullOrWhiteSpace(fromEnv))
            return fromEnv;

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return System.IO.Path.Combine(home, ".tunebox", "library.json");
    }
}
