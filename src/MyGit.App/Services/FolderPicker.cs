using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace MyGit.App.Services;

/// <summary>
/// <see cref="IFolderPicker"/> that shows the native Windows folder dialog.
/// </summary>
public sealed class FolderPicker : IFolderPicker
{
    private readonly TopLevel _owner;

    public FolderPicker(TopLevel owner)
    {
        _owner = owner;
    }

    public async Task<string?> PickFolderAsync(string title)
    {
        var folders = await _owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
        });

        return folders.Count == 0 ? null : folders[0].TryGetLocalPath();
    }
}
