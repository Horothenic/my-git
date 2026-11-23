using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input.Platform;

namespace MyGit.App.Services;

/// <summary>
/// <see cref="IShellService"/> for Windows: default browser and the window's clipboard.
/// </summary>
public sealed class ShellService : IShellService
{
    private readonly TopLevel _owner;

    public ShellService(TopLevel owner)
    {
        _owner = owner;
    }

    public void OpenUrl(string url) =>
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    public void OpenFile(string path)
    {
        if (File.Exists(path))
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
    }

    public void ShowInExplorer(string path)
    {
        // A deleted file can't be selected; open its folder instead.
        if (File.Exists(path) || Directory.Exists(path))
        {
            Process.Start("explorer.exe", $"/select,\"{path}\"");
        }
        else if (Path.GetDirectoryName(path) is { } folder && Directory.Exists(folder))
        {
            Process.Start("explorer.exe", $"\"{folder}\"");
        }
    }

    public async Task CopyTextAsync(string text)
    {
        if (_owner.Clipboard is not null)
        {
            await _owner.Clipboard.SetTextAsync(text);
        }
    }
}
