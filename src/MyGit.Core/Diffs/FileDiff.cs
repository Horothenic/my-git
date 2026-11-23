namespace MyGit.Core.Diffs;

public enum DiffLineKind
{
    Context,
    Added,
    Removed,
}

/// <summary>One line of a unified diff.</summary>
/// <param name="OldNumber">Line number in the old version, or null for added lines.</param>
/// <param name="NewNumber">Line number in the new version, or null for removed lines.</param>
public sealed record DiffLine(DiffLineKind Kind, string Text, int? OldNumber, int? NewNumber);

/// <summary>One block of changes ("@@ -12,3 +12,4 @@") with its surrounding context lines.</summary>
public sealed record DiffHunk(string Header, int OldStart, int NewStart, IReadOnlyList<DiffLine> Lines);

/// <summary>
/// The diff of one file, ready to show.
/// </summary>
public sealed record FileDiff(string Path, IReadOnlyList<DiffHunk> Hunks, bool IsBinary)
{
    public static FileDiff Binary(string path) => new(path, [], IsBinary: true);

    public bool IsEmpty => !IsBinary && Hunks.Count == 0;
}
