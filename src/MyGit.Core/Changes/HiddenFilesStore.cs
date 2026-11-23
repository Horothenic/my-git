using System.Text.Json;

namespace MyGit.Core.Changes;

/// <summary>
/// Files and folders the user hid from the Unstaged list (the H key). Visual only:
/// git still tracks them, and nothing is written to .gitignore.
/// Saved per repository in %APPDATA%\MyGit\hidden.json.
/// </summary>
public sealed class HiddenFilesStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _filePath;

    // Repository path → hidden entries. Folder entries end with "/", e.g. "ProjectSettings/".
    private readonly Dictionary<string, SortedSet<string>> _hidden = new(StringComparer.OrdinalIgnoreCase);

    public HiddenFilesStore(string filePath)
    {
        _filePath = filePath;
        Load();
    }

    public static string DefaultFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MyGit",
        "hidden.json");

    /// <summary>The hidden entries of one repository.</summary>
    public IReadOnlyCollection<string> GetEntries(string repositoryPath) =>
        _hidden.TryGetValue(repositoryPath, out var entries) ? entries : [];

    /// <summary>True when <paramref name="path"/> is hidden itself or sits inside a hidden folder.</summary>
    public bool IsHidden(string repositoryPath, string path)
    {
        if (!_hidden.TryGetValue(repositoryPath, out var entries))
        {
            return false;
        }

        return entries.Any(entry => Covers(entry, path));
    }

    /// <summary>Hides files (by path) and folders (by path ending in "/").</summary>
    public void Hide(string repositoryPath, IEnumerable<string> entries)
    {
        if (!_hidden.TryGetValue(repositoryPath, out var set))
        {
            set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            _hidden[repositoryPath] = set;
        }

        foreach (var entry in entries)
        {
            set.Add(entry);
        }

        Save();
    }

    /// <summary>
    /// Unhides the given files and folders. Anything that hides them goes, including a hidden
    /// parent folder (which brings back everything in that folder).
    /// </summary>
    public void Unhide(string repositoryPath, IEnumerable<string> paths)
    {
        if (!_hidden.TryGetValue(repositoryPath, out var set))
        {
            return;
        }

        foreach (var path in paths)
        {
            set.RemoveWhere(entry => Covers(entry, path) || (path.EndsWith('/') && entry.StartsWith(path, StringComparison.OrdinalIgnoreCase)));
        }

        if (set.Count == 0)
        {
            _hidden.Remove(repositoryPath);
        }

        Save();
    }

    private static bool Covers(string entry, string path) =>
        entry.EndsWith('/')
            ? path.StartsWith(entry, StringComparison.OrdinalIgnoreCase) || string.Equals(path, entry, StringComparison.OrdinalIgnoreCase)
            : string.Equals(entry, path, StringComparison.OrdinalIgnoreCase);

    private void Load()
    {
        if (!File.Exists(_filePath))
        {
            return;
        }

        try
        {
            var loaded = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(File.ReadAllText(_filePath), JsonOptions);

            foreach (var (repository, entries) in loaded ?? [])
            {
                _hidden[repository] = new SortedSet<string>(entries, StringComparer.OrdinalIgnoreCase);
            }
        }
        catch (JsonException)
        {
            // A damaged file shouldn't stop the app: start with nothing hidden.
        }
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        var data = _hidden.ToDictionary(pair => pair.Key, pair => pair.Value.ToList());
        File.WriteAllText(_filePath, JsonSerializer.Serialize(data, JsonOptions));
    }
}
