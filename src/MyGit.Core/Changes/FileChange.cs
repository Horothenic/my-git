namespace MyGit.Core.Changes;

/// <summary>
/// How a file differs, as one letter in the file lists.
/// </summary>
public enum ChangeKind
{
    Modified,     // M
    Added,        // A (staged new file)
    Deleted,      // D
    Renamed,      // R
    Copied,       // C
    TypeChanged,  // T (e.g. file became a symlink)
    Untracked,    // ? (new file git doesn't know yet)
    Conflicted,   // ! (merge or rebase conflict)
}

/// <summary>
/// One file in the Unstaged or Staged list.
/// </summary>
/// <param name="Path">Path relative to the repository root, with forward slashes.</param>
/// <param name="OriginalPath">For renames and copies: the old path.</param>
public sealed record FileChange(string Path, ChangeKind Kind, string? OriginalPath = null)
{
    public string Letter => Kind switch
    {
        ChangeKind.Modified => "M",
        ChangeKind.Added => "A",
        ChangeKind.Deleted => "D",
        ChangeKind.Renamed => "R",
        ChangeKind.Copied => "C",
        ChangeKind.TypeChanged => "T",
        ChangeKind.Untracked => "?",
        ChangeKind.Conflicted => "!",
        _ => " ",
    };

    public string FileName => System.IO.Path.GetFileName(Path);
}

/// <summary>
/// The working tree state: what's changed but not staged, and what's staged.
/// A file can be in both lists when only some of its changes are staged.
/// </summary>
public sealed record WorkingTreeStatus(IReadOnlyList<FileChange> Unstaged, IReadOnlyList<FileChange> Staged)
{
    public static readonly WorkingTreeStatus Empty = new([], []);
}
