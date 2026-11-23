namespace MyGit.Core.Diffs;

public enum DiffLineKind
{
    Context,
    Added,
    Removed,
}

/// <summary>One line of a unified diff.</summary>
/// <param name="Text">The line for display (without a trailing carriage return).</param>
/// <param name="OldNumber">Line number in the old version, or null for added lines.</param>
/// <param name="NewNumber">Line number in the new version, or null for removed lines.</param>
/// <param name="Content">
/// The line exactly as git printed it after the +/-/space prefix, including a trailing '\r'
/// for Windows line endings. Patches are built from this so they match the file byte for byte.
/// </param>
/// <param name="NoNewlineAtEnd">True when git marked this line "\ No newline at end of file".</param>
public sealed record DiffLine(
    DiffLineKind Kind,
    string Text,
    int? OldNumber,
    int? NewNumber,
    string? Content = null,
    bool NoNewlineAtEnd = false)
{
    /// <summary>The exact content for patches (falls back to <see cref="Text"/>).</summary>
    public string ExactContent => Content ?? Text;

    public bool IsChange => Kind != DiffLineKind.Context;
}

/// <summary>One block of changes ("@@ -12,3 +12,4 @@") with its surrounding context lines.</summary>
public sealed record DiffHunk(string Header, int OldStart, int NewStart, IReadOnlyList<DiffLine> Lines);

/// <summary>
/// The diff of one file, ready to show.
/// </summary>
/// <param name="RawText">What git printed (or the file content for new files). Used to tell whether the diff changed.</param>
public sealed record FileDiff(string Path, IReadOnlyList<DiffHunk> Hunks, bool IsBinary, string RawText = "")
{
    public static FileDiff Binary(string path) => new(path, [], IsBinary: true);

    public bool IsEmpty => !IsBinary && Hunks.Count == 0;
}
