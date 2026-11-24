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

    /// <summary>The code editor "Open in editor" uses (from Settings, else the first one installed), e.g. "VS Code"; null when none is found.</summary>
    string? EditorName { get; }

    /// <summary>The terminal "Open in terminal" uses, e.g. "Windows Terminal".</summary>
    string? TerminalName { get; }

    /// <summary>Opens a file or folder in the code editor; without one, a file opens in its default program.</summary>
    void OpenInEditor(string path);

    /// <summary>Opens a terminal in the folder.</summary>
    void OpenTerminal(string folder);
}
