using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using MyGit.Core.Settings;
using MyGit.Core.Tools;

namespace MyGit.App.Services;

/// <summary>
/// <see cref="IShellService"/> for Windows: default browser, the window's clipboard, and your editor and terminal.
/// </summary>
public sealed class ShellService : IShellService
{
    private readonly TopLevel _owner;
    private readonly AppSettingsStore _settings;

    public ShellService(TopLevel owner, AppSettingsStore settings)
    {
        _owner = owner;
        _settings = settings;
    }

    public string? EditorName => Editor?.Name;

    public string? TerminalName => Terminal?.Name;

    private ExternalTool? Editor => ToolLocator.Pick(ToolLocator.Editors, _settings.Settings.Editor);

    private ExternalTool? Terminal => ToolLocator.Pick(ToolLocator.Terminals, _settings.Settings.Terminal);

    public void OpenInEditor(string path)
    {
        if (Editor is { } editor && (File.Exists(path) || Directory.Exists(path)))
        {
            Process.Start(new ProcessStartInfo(editor.Path) { ArgumentList = { path }, UseShellExecute = false });
        }
        else
        {
            OpenFile(path);
        }
    }

    public void OpenTerminal(string folder)
    {
        if (Terminal is not { } terminal || !Directory.Exists(folder))
        {
            return;
        }

        var start = new ProcessStartInfo(terminal.Path) { WorkingDirectory = folder, UseShellExecute = true };
        switch (terminal.Id)
        {
            case "windowsterminal":
                start.ArgumentList.Add("-d");
                start.ArgumentList.Add(folder);
                start.UseShellExecute = false;
                break;
            case "gitbash":
                start.ArgumentList.Add($"--cd={folder}");
                start.UseShellExecute = false;
                break;
        }

        Process.Start(start);
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
