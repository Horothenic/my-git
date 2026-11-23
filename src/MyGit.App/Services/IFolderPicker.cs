namespace MyGit.App.Services;

/// <summary>
/// Lets view models ask the user for a folder without knowing about windows.
/// </summary>
public interface IFolderPicker
{
    /// <summary>Returns the chosen folder, or null if the user cancelled.</summary>
    Task<string?> PickFolderAsync(string title);
}
