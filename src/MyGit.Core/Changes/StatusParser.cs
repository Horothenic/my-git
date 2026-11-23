namespace MyGit.Core.Changes;

/// <summary>
/// Parses "git status --porcelain=v2 -z" output.
/// https://git-scm.com/docs/git-status#_porcelain_format_version_2
/// </summary>
public static class StatusParser
{
    public static WorkingTreeStatus Parse(string output)
    {
        var unstaged = new List<FileChange>();
        var staged = new List<FileChange>();

        // Entries are separated by NUL. A rename entry is followed by one extra entry: its old path.
        var entries = output.Split('\0', StringSplitOptions.RemoveEmptyEntries);

        for (var i = 0; i < entries.Length; i++)
        {
            var entry = entries[i];

            switch (entry[0])
            {
                case '1':
                {
                    // 1 XY sub mH mI mW hH hI path
                    var parts = entry.Split(' ', 9);
                    AddOrdinary(parts[1], parts[8], null, unstaged, staged);
                    break;
                }
                case '2':
                {
                    // 2 XY sub mH mI mW hH hI Xscore path  (then NUL + original path)
                    var parts = entry.Split(' ', 10);
                    var originalPath = i + 1 < entries.Length ? entries[++i] : null;
                    AddOrdinary(parts[1], parts[9], originalPath, unstaged, staged);
                    break;
                }
                case 'u':
                {
                    // u XY sub m1 m2 m3 mW h1 h2 h3 path: a conflict, shown once in Unstaged.
                    var parts = entry.Split(' ', 11);
                    unstaged.Add(new FileChange(parts[10], ChangeKind.Conflicted));
                    break;
                }
                case '?':
                    unstaged.Add(new FileChange(entry[2..], ChangeKind.Untracked));
                    break;
            }
        }

        return new WorkingTreeStatus(Sort(unstaged), Sort(staged));
    }

    private static void AddOrdinary(string xy, string path, string? originalPath, List<FileChange> unstaged, List<FileChange> staged)
    {
        var index = xy[0];
        var workTree = xy[1];

        if (index != '.')
        {
            staged.Add(new FileChange(path, KindOf(index), index is 'R' or 'C' ? originalPath : null));
        }

        if (workTree != '.')
        {
            unstaged.Add(new FileChange(path, KindOf(workTree)));
        }
    }

    private static ChangeKind KindOf(char code) => code switch
    {
        'A' => ChangeKind.Added,
        'D' => ChangeKind.Deleted,
        'R' => ChangeKind.Renamed,
        'C' => ChangeKind.Copied,
        'T' => ChangeKind.TypeChanged,
        _ => ChangeKind.Modified,
    };

    private static List<FileChange> Sort(List<FileChange> changes) =>
        changes.OrderBy(change => change.Path, StringComparer.OrdinalIgnoreCase).ToList();
}
