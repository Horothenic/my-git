using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MyGit.Core.Changes;

namespace MyGit.App.ViewModels.Changes;

/// <summary>
/// A folder or file in the Unstaged or Staged tree.
/// </summary>
public sealed partial class FileTreeNodeViewModel : ObservableObject
{
    public FileTreeNodeViewModel(string name, string path, bool isFolder)
    {
        Name = name;
        Path = path;
        IsFolder = isFolder;
    }

    public string Name { get; }

    /// <summary>Repository-relative path with forward slashes (folders without a trailing slash).</summary>
    public string Path { get; }

    public bool IsFolder { get; }

    public bool IsFile => !IsFolder;

    public ObservableCollection<FileTreeNodeViewModel> Children { get; } = [];

    /// <summary>The change this file stands for (null for folders).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Letter), nameof(ToolTip), nameof(IsAdded), nameof(IsDeleted), nameof(IsConflicted))]
    public partial FileChange? Change { get; set; }

    public string Letter => Change?.Letter ?? string.Empty;

    public bool IsAdded => Change?.Kind is ChangeKind.Added or ChangeKind.Untracked;

    public bool IsDeleted => Change?.Kind == ChangeKind.Deleted;

    public bool IsConflicted => Change?.Kind == ChangeKind.Conflicted;

    public string ToolTip => Change?.OriginalPath is { } original ? $"{original} → {Path}" : Path;

    /// <summary>Number of changed files inside a folder (shown after its name).</summary>
    [ObservableProperty]
    public partial int FileCount { get; set; }

    [ObservableProperty]
    public partial bool IsExpanded { get; set; } = true;

    /// <summary>Every file at or below this node.</summary>
    public IEnumerable<FileTreeNodeViewModel> Files()
    {
        if (IsFile)
        {
            yield return this;
            yield break;
        }

        foreach (var child in Children)
        {
            foreach (var file in child.Files())
            {
                yield return file;
            }
        }
    }
}
