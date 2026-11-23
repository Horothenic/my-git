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
/// <param name="LeftIndex">Index of <paramref name="Left"/> in its hunk's lines (for picking lines to stage).</param>
/// <param name="RightIndex">Index of <paramref name="Right"/> in its hunk's lines.</param>
public sealed record SideBySideRow(
    SideBySideRowKind Kind,
    DiffLine? Left,
    DiffLine? Right,
    int HunkIndex,
    string? HunkHeader = null,
    int LeftIndex = -1,
    int RightIndex = -1);

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

            var removed = new List<(DiffLine Line, int Index)>();
            var added = new List<(DiffLine Line, int Index)>();

            for (var index = 0; index < hunk.Lines.Count; index++)
            {
                var line = hunk.Lines[index];

                if (line.Kind == DiffLineKind.Context)
                {
                    FlushChanges();
                    rows.Add(new SideBySideRow(SideBySideRowKind.Context, line, line, hunkIndex, LeftIndex: index, RightIndex: index));
                }
                else if (line.Kind == DiffLineKind.Removed)
                {
                    // A removal after additions starts a new change block.
                    if (added.Count > 0)
                    {
                        FlushChanges();
                    }

                    removed.Add((line, index));
                }
                else
                {
                    added.Add((line, index));
                }
            }

            FlushChanges();

            void FlushChanges()
            {
                var count = Math.Max(removed.Count, added.Count);
                for (var i = 0; i < count; i++)
                {
                    var left = i < removed.Count ? removed[i] : default;
                    var right = i < added.Count ? added[i] : default;

                    rows.Add(new SideBySideRow(
                        SideBySideRowKind.Change,
                        i < removed.Count ? left.Line : null,
                        i < added.Count ? right.Line : null,
                        hunkIndex,
                        LeftIndex: i < removed.Count ? left.Index : -1,
                        RightIndex: i < added.Count ? right.Index : -1));
                }

                removed.Clear();
                added.Clear();
            }
        }

        return rows;
    }
}
