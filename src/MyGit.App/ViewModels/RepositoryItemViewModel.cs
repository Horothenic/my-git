using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyGit.Core.Repositories;

namespace MyGit.App.ViewModels;

/// <summary>
/// One row in the repository dropdown.
/// </summary>
public sealed partial class RepositoryItemViewModel : ObservableObject
{
    private readonly Action<RepositoryItemViewModel> _open;
    private readonly Action<RepositoryItemViewModel> _remove;

    public RepositoryItemViewModel(
        RepositoryInfo info,
        Action<RepositoryItemViewModel> open,
        Action<RepositoryItemViewModel> remove)
    {
        Info = info;
        _open = open;
        _remove = remove;
    }

    public RepositoryInfo Info { get; }

    public string Name => Info.Name;

    public string Path => Info.Path;

    /// <summary>True when the folder was moved or deleted outside the app.</summary>
    public bool IsMissing => !Directory.Exists(Info.Path);

    [ObservableProperty]
    public partial bool IsCurrent { get; set; }

    [RelayCommand]
    private void Open() => _open(this);

    [RelayCommand]
    private void Remove() => _remove(this);

    [RelayCommand]
    private void ShowInExplorer()
    {
        if (!IsMissing)
        {
            System.Diagnostics.Process.Start("explorer.exe", Info.Path);
        }
    }
}
