using System.Collections.ObjectModel;
using MyGit.Core.Changes;

namespace MyGit.App.ViewModels.Changes;

/// <summary>
/// The nodes of one file list (Unstaged or Staged), as a folder tree or a flat list.
///
/// <see cref="Update"/> changes the existing nodes in place instead of rebuilding them, so
/// the tree keeps its selection and expanded folders when the list refreshes in the background.
/// </summary>
public sealed class FileTree
{
    public ObservableCollection<FileTreeNodeViewModel> Roots { get; } = [];

    /// <summary>All file nodes, in display order.</summary>
    public IEnumerable<FileTreeNodeViewModel> Files => Roots.SelectMany(root => root.Files());

    public int FileCount { get; private set; }

    public void Update(IReadOnlyList<FileChange> changes, bool flat)
    {
        var desired = flat ? BuildFlat(changes) : BuildTree(changes);
        Merge(Roots, desired);
        FileCount = changes.Count;
    }

    public FileTreeNodeViewModel? FindFile(string path) =>
        Files.FirstOrDefault(file => string.Equals(file.Path, path, StringComparison.Ordinal));

    /// <summary>What a node should look like after the update.</summary>
    private sealed record Desired(string Name, string Path, bool IsFolder, FileChange? Change, List<Desired> Children)
    {
        public int CountFiles() => IsFolder ? Children.Sum(child => child.CountFiles()) : 1;
    }

    private static List<Desired> BuildFlat(IReadOnlyList<FileChange> changes) =>
        changes.Select(change => new Desired(change.Path, change.Path, false, change, [])).ToList();

    private static List<Desired> BuildTree(IReadOnlyList<FileChange> changes)
    {
        var roots = new List<Desired>();

        foreach (var change in changes)
        {
            var parts = change.Path.Split('/');
            var level = roots;
            var folderPath = string.Empty;

            for (var i = 0; i < parts.Length - 1; i++)
            {
                folderPath = folderPath.Length == 0 ? parts[i] : $"{folderPath}/{parts[i]}";
                var folder = level.FirstOrDefault(node => node.IsFolder && node.Name == parts[i]);

                if (folder is null)
                {
                    folder = new Desired(parts[i], folderPath, true, null, []);
                    level.Add(folder);
                }

                level = folder.Children;
            }

            level.Add(new Desired(parts[^1], change.Path, false, change, []));
        }

        return roots;
    }

    // Folders first, then files, each alphabetically (like Explorer).
    private static int Compare(bool aIsFolder, string aName, bool bIsFolder, string bName) =>
        aIsFolder != bIsFolder ? (aIsFolder ? -1 : 1) : string.Compare(aName, bName, StringComparison.OrdinalIgnoreCase);

    private static void Merge(ObservableCollection<FileTreeNodeViewModel> existing, List<Desired> desired)
    {
        desired.Sort((a, b) => Compare(a.IsFolder, a.Name, b.IsFolder, b.Name));

        // Remove nodes that are gone.
        for (var i = existing.Count - 1; i >= 0; i--)
        {
            var node = existing[i];
            if (!desired.Any(d => d.IsFolder == node.IsFolder && d.Path == node.Path))
            {
                existing.RemoveAt(i);
            }
        }

        // Update the ones that stay and insert new ones in sorted position.
        for (var i = 0; i < desired.Count; i++)
        {
            var want = desired[i];
            var node = existing.FirstOrDefault(n => n.IsFolder == want.IsFolder && n.Path == want.Path);

            if (node is null)
            {
                node = new FileTreeNodeViewModel(want.Name, want.Path, want.IsFolder);
                existing.Insert(Math.Min(i, existing.Count), node);
            }
            else if (existing.IndexOf(node) != i)
            {
                existing.Move(existing.IndexOf(node), i);
            }

            node.Change = want.Change;
            node.FileCount = want.CountFiles();

            if (want.IsFolder)
            {
                Merge(node.Children, want.Children);
            }
        }
    }
}
