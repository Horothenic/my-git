namespace MyGit.Core.Platform;

/// <summary>Where My Git keeps its own files (settings, the repository list, hidden files), per operating system.</summary>
public static class AppPaths
{
    /// <summary>
    /// Windows: %AppData%\MyGit. macOS: ~/Library/Application Support/My Git (where Mac apps keep their data).
    /// Elsewhere: the user's config folder.
    /// </summary>
    public static string DataFolder => OperatingSystem.IsMacOS()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "My Git")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MyGit");

    /// <summary>A file in <see cref="DataFolder"/>, e.g. "settings.json".</summary>
    public static string DataFile(string name) => Path.Combine(DataFolder, name);
}
