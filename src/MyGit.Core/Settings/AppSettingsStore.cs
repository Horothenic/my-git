using System.Text.Json;

namespace MyGit.Core.Settings;

/// <summary>
/// Loads and saves <see cref="AppSettings"/> as JSON
/// (by default %APPDATA%\MyGit\settings.json).
/// </summary>
public sealed class AppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _filePath;

    public AppSettingsStore(string filePath)
    {
        _filePath = filePath;
        Settings = Load();
    }

    public static string DefaultFilePath => Platform.AppPaths.DataFile("settings.json");

    public AppSettings Settings { get; }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        File.WriteAllText(_filePath, JsonSerializer.Serialize(Settings, JsonOptions));
    }

    private AppSettings Load()
    {
        if (!File.Exists(_filePath))
        {
            return new AppSettings();
        }

        try
        {
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_filePath), JsonOptions) ?? new AppSettings();
        }
        catch (JsonException)
        {
            // A corrupted settings file should never stop the app from starting.
            return new AppSettings();
        }
    }
}
