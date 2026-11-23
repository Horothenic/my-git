using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyGit.App.Services;
using MyGit.Core.Repositories;

namespace MyGit.App.ViewModels;

/// <summary>
/// The "Repository" dropdown: list, filter, add and remove repositories.
/// </summary>
public sealed partial class RepositoryPickerViewModel : ObservableObject
{
    private readonly RepositoryStore _store;
    private readonly RepositoryService _repositoryService;
    private readonly IFolderPicker _folderPicker;
    private readonly List<RepositoryItemViewModel> _allItems = [];

    public RepositoryPickerViewModel(
        RepositoryStore store,
        RepositoryService repositoryService,
        IFolderPicker folderPicker)
    {
        _store = store;
        _repositoryService = repositoryService;
        _folderPicker = folderPicker;

        Reload();
    }

    /// <summary>Raised when the user picks a repository to open.</summary>
    public event Action<RepositoryItemViewModel>? RepositoryOpened;

    /// <summary>Raised when the open repository was removed from the list.</summary>
    public event Action? CurrentRepositoryRemoved;

    /// <summary>Raised when the dropdown should close (after opening or adding).</summary>
    public event Action? CloseRequested;

    /// <summary>Raised with a message the main window should show to the user.</summary>
    public event Action<string>? MessageRaised;

    /// <summary>The rows shown, after applying <see cref="FilterText"/>.</summary>
    public ObservableCollection<RepositoryItemViewModel> Items { get; } = [];

    public bool HasRepositories => _allItems.Count > 0;

    [ObservableProperty]
    public partial string FilterText { get; set; } = string.Empty;

    public RepositoryItemViewModel? Find(string path) =>
        _allItems.FirstOrDefault(item => string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase));

    public void MarkCurrent(RepositoryItemViewModel? current)
    {
        foreach (var item in _allItems)
        {
            item.IsCurrent = item == current;
        }
    }

    [RelayCommand]
    private async Task AddLocalAsync()
    {
        var folder = await _folderPicker.PickFolderAsync("Add a local repository");

        if (folder is null)
        {
            return;
        }

        var root = await _repositoryService.FindRootAsync(folder);

        if (root is null)
        {
            MessageRaised?.Invoke($"\"{folder}\" isn't a git repository.");
            return;
        }

        _store.Add(RepositoryInfo.FromPath(root));
        Reload();

        var added = Find(root);
        if (added is not null)
        {
            Open(added);
        }
    }

    partial void OnFilterTextChanged(string value) => ApplyFilter();

    private void Open(RepositoryItemViewModel item)
    {
        if (item.IsMissing)
        {
            MessageRaised?.Invoke($"\"{item.Path}\" no longer exists. Remove it from the list, or restore the folder.");
            return;
        }

        CloseRequested?.Invoke();
        RepositoryOpened?.Invoke(item);
    }

    private void Remove(RepositoryItemViewModel item)
    {
        var wasCurrent = item.IsCurrent;

        _store.Remove(item.Path);
        Reload();

        if (wasCurrent)
        {
            CurrentRepositoryRemoved?.Invoke();
        }
    }

    private void Reload()
    {
        var currentPath = _allItems.FirstOrDefault(item => item.IsCurrent)?.Path;

        _allItems.Clear();
        foreach (var info in _store.Repositories)
        {
            var item = new RepositoryItemViewModel(info, Open, Remove);
            item.IsCurrent = string.Equals(info.Path, currentPath, StringComparison.OrdinalIgnoreCase);
            _allItems.Add(item);
        }

        OnPropertyChanged(nameof(HasRepositories));
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        Items.Clear();

        foreach (var item in _allItems)
        {
            if (FilterText.Length == 0 || item.Name.Contains(FilterText, StringComparison.OrdinalIgnoreCase))
            {
                Items.Add(item);
            }
        }
    }
}
