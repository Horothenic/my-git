using System.Text.Json;

namespace MyGit.Core.Repositories;

/// <summary>
/// Remembers the list of repositories in a JSON file
/// (by default %APPDATA%\MyGit\repositories.json).
/// </summary>
public sealed class RepositoryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _filePath;
    private readonly List<RepositoryInfo> _repositories = [];

    public RepositoryStore(string filePath)
    {
        _filePath = filePath;
        Load();
    }

    public static string DefaultFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MyGit",
        "repositories.json");

    public IReadOnlyList<RepositoryInfo> Repositories => _repositories;

    /// <summary>Adds a repository. Returns false if it is already in the list.</summary>
    public bool Add(RepositoryInfo repository)
    {
        if (Contains(repository.Path))
        {
            return false;
        }

        _repositories.Add(repository);
        _repositories.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        Save();
        return true;
    }

    public bool Remove(string path)
    {
        var removed = _repositories.RemoveAll(r => SamePath(r.Path, path)) > 0;

        if (removed)
        {
            Save();
        }

        return removed;
    }

    public bool Contains(string path) => _repositories.Any(r => SamePath(r.Path, path));

    private void Load()
    {
        if (!File.Exists(_filePath))
        {
            return;
        }

        var json = File.ReadAllText(_filePath);
        var loaded = JsonSerializer.Deserialize<List<RepositoryInfo>>(json, JsonOptions);

        if (loaded is not null)
        {
            _repositories.AddRange(loaded);
        }
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        File.WriteAllText(_filePath, JsonSerializer.Serialize(_repositories, JsonOptions));
    }

    // Windows paths are case-insensitive.
    private static bool SamePath(string a, string b) =>
        string.Equals(
            Path.GetFullPath(a).TrimEnd('\\', '/'),
            Path.GetFullPath(b).TrimEnd('\\', '/'),
            StringComparison.OrdinalIgnoreCase);
}
