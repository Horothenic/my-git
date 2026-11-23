namespace MyGit.Core.Diffs;

public enum SideBySideRowKind
{
    /// <summary>"@@ line 12 @@" separator between hunks.</summary>
    HunkHeader,

    /// <summary>Same text on both sides.</summary>
    Context,

    /// <summary>Removed on the left and/or added on the right.</summary>
    Change,
}

/// <summary>
/// One row of the two-column diff. A side is null when that side has no line on this row
/// (for example a pure addition has nothing on the left).
/// </summary>
public sealed record SideBySideRow(SideBySideRowKind Kind, DiffLine? Left, DiffLine? Right, int HunkIndex, string? HunkHeader = null);

/// <summary>
/// Lays a <see cref="FileDiff"/> out in two columns, old on the left and new on the right.
/// Consecutive removed and added lines are paired up row by row so edits sit side by side.
/// </summary>
public static class SideBySide
{
    public static IReadOnlyList<SideBySideRow> Build(FileDiff diff)
    {
        var rows = new List<SideBySideRow>();

        for (var hunkIndex = 0; hunkIndex < diff.Hunks.Count; hunkIndex++)
        {
            var hunk = diff.Hunks[hunkIndex];
            rows.Add(new SideBySideRow(SideBySideRowKind.HunkHeader, null, null, hunkIndex, $"@@ line {hunk.NewStart} @@"));

            var removed = new List<DiffLine>();
            var added = new List<DiffLine>();

            foreach (var line in hunk.Lines)
            {
                if (line.Kind == DiffLineKind.Context)
                {
                    FlushChanges();
                    rows.Add(new SideBySideRow(SideBySideRowKind.Context, line, line, hunkIndex));
                }
                else if (line.Kind == DiffLineKind.Removed)
                {
                    // A removal after additions starts a new change block.
                    if (added.Count > 0)
                    {
                        FlushChanges();
                    }

                    removed.Add(line);
                }
                else
                {
                    added.Add(line);
                }
            }

            FlushChanges();

            void FlushChanges()
            {
                var count = Math.Max(removed.Count, added.Count);
                for (var i = 0; i < count; i++)
                {
                    rows.Add(new SideBySideRow(
                        SideBySideRowKind.Change,
                        i < removed.Count ? removed[i] : null,
                        i < added.Count ? added[i] : null,
                        hunkIndex));
                }

                removed.Clear();
                added.Clear();
            }
        }

        return rows;
    }
}
