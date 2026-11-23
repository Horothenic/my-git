namespace MyGit.App.Services;

/// <summary>
/// Things view models need from the operating system: the browser and the clipboard.
/// </summary>
public interface IShellService
{
    void OpenUrl(string url);

    Task CopyTextAsync(string text);
}
