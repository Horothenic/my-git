namespace MyGit.App.Services;

/// <summary>
/// Things view models need from the operating system: the browser and the clipboard.
/// </summary>
public interface IShellService
{
    void OpenUrl(string url);

    Task CopyTextAsync(string text);

    /// <summary>Opens a file with its default program (e.g. your code editor for .cs files).</summary>
    void OpenFile(string path);

    /// <summary>Opens Explorer with the file selected.</summary>
    void ShowInExplorer(string path);
}
