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

    public async Task CopyTextAsync(string text)
    {
        if (_owner.Clipboard is not null)
        {
            await _owner.Clipboard.SetTextAsync(text);
        }
    }
}
