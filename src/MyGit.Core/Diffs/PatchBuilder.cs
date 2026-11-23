using System.Text;

namespace MyGit.Core.Diffs;

/// <summary>A line in a <see cref="FileDiff"/>: which hunk, and which line inside it.</summary>
public readonly record struct DiffLineRef(int HunkIndex, int LineIndex);

/// <summary>
/// Builds a patch that contains only some of a diff's changed lines, for "git apply".
/// This is how staging a hunk or single lines works (the same idea as "git add -p").
///
/// Forward patches (staging from the Unstaged diff) keep every old line, so they apply to the
/// file as it is in the index: a removal that isn't selected becomes an unchanged line, and an
/// addition that isn't selected is left out.
///
/// Reverse patches (unstaging from the Staged diff, or discarding from the Unstaged diff) are
/// applied with --reverse, so they keep every new line instead: an addition that isn't selected
/// becomes an unchanged line, and a removal that isn't selected is left out.
/// </summary>
public static class PatchBuilder
{
    /// <summary>
    /// Returns the patch text, or null when no changed line is selected.
    /// </summary>
    /// <param name="isNewFile">True for a file git doesn't track yet (its patch creates the file).</param>
    public static string? Build(FileDiff diff, IReadOnlySet<DiffLineRef> selected, bool reverse, bool isNewFile = false)
    {
        var body = new StringBuilder();
        var offset = 0; // how much earlier hunks in this patch moved the line numbers

        for (var hunkIndex = 0; hunkIndex < diff.Hunks.Count; hunkIndex++)
        {
            var hunk = diff.Hunks[hunkIndex];
            var lines = new List<(char Prefix, DiffLine Line)>();
            var hasSelectedChange = false;
            var block = new List<(DiffLine Line, bool Selected)>();

            for (var lineIndex = 0; lineIndex < hunk.Lines.Count; lineIndex++)
            {
                var line = hunk.Lines[lineIndex];

                if (line.Kind == DiffLineKind.Context)
                {
                    EmitBlock();
                    lines.Add((' ', line));
                }
                else
                {
                    var isSelected = selected.Contains(new DiffLineRef(hunkIndex, lineIndex));
                    hasSelectedChange |= isSelected;
                    block.Add((line, isSelected));
                }
            }

            EmitBlock();

            // One run of removed and added lines between unchanged lines. The side of the file
            // the patch applies to must keep its original order, and the picked lines must land
            // next to each other: e.g. picking "-b" and "+B" out of "-b -c +B +c" gives "-b +B  c".
            void EmitBlock()
            {
                if (block.Count == 0)
                {
                    return;
                }

                // Forward: every removal stays (picked ones as '-', the rest as unchanged ' '),
                // and picked additions go right after the last picked removal.
                // Reverse is the mirror image: every addition stays, and picked removals go
                // right before the first picked addition.
                var keep = reverse ? DiffLineKind.Added : DiffLineKind.Removed;
                var move = reverse ? DiffLineKind.Removed : DiffLineKind.Added;
                var keepPrefix = reverse ? '+' : '-';
                var movePrefix = reverse ? '-' : '+';

                var kept = block.Where(entry => entry.Line.Kind == keep).ToList();
                var moved = block.Where(entry => entry.Line.Kind == move && entry.Selected)
                                 .Select(entry => (movePrefix, entry.Line)).ToList();

                var anchor = reverse
                    ? kept.FindIndex(entry => entry.Selected)          // before the first picked addition
                    : kept.FindLastIndex(entry => entry.Selected) + 1; // after the last picked removal

                if (anchor < 0 || (!reverse && anchor == 0))
                {
                    // Nothing picked on the kept side: new lines go after the unchanged ones (forward)
                    // or picked removals go before them (reverse).
                    anchor = reverse ? 0 : kept.Count;
                }

                for (var i = 0; i <= kept.Count; i++)
                {
                    if (i == anchor)
                    {
                        lines.AddRange(moved);
                    }

                    if (i < kept.Count)
                    {
                        lines.Add((kept[i].Selected ? keepPrefix : ' ', kept[i].Line));
                    }
                }

                block.Clear();
            }

            if (!hasSelectedChange)
            {
                continue;
            }

            var oldCount = lines.Count(entry => entry.Prefix is ' ' or '-');
            var newCount = lines.Count(entry => entry.Prefix is ' ' or '+');

            // The side that isn't edited keeps git's numbers; the other side shifts with earlier hunks.
            var oldStart = reverse ? Math.Max(hunk.NewStart + offset, 0) : hunk.OldStart;
            var newStart = reverse ? hunk.NewStart : Math.Max(hunk.OldStart + offset, 0);
            if (isNewFile)
            {
                oldStart = 0;
                newStart = 1;
            }

            offset += reverse ? oldCount - newCount : newCount - oldCount;

            body.Append(CultureInvariant($"@@ -{oldStart},{oldCount} +{newStart},{newCount} @@\n"));
            foreach (var (prefix, line) in lines)
            {
                body.Append(prefix).Append(line.ExactContent).Append('\n');
                if (line.NoNewlineAtEnd)
                {
                    body.Append("\\ No newline at end of file\n");
                }
            }
        }

        if (body.Length == 0)
        {
            return null;
        }

        var path = diff.Path;
        var header = isNewFile
            ? $"diff --git a/{path} b/{path}\nnew file mode 100644\n--- /dev/null\n+++ b/{path}\n"
            : $"diff --git a/{path} b/{path}\n--- a/{path}\n+++ b/{path}\n";

        return header + body;
    }

    /// <summary>Every changed line of a diff (used to tell "the whole file is selected").</summary>
    public static IReadOnlySet<DiffLineRef> AllChanges(FileDiff diff, int? onlyHunk = null)
    {
        var all = new HashSet<DiffLineRef>();

        for (var hunkIndex = 0; hunkIndex < diff.Hunks.Count; hunkIndex++)
        {
            if (onlyHunk is { } only && only != hunkIndex)
            {
                continue;
            }

            var lines = diff.Hunks[hunkIndex].Lines;
            for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
            {
                if (lines[lineIndex].IsChange)
                {
                    all.Add(new DiffLineRef(hunkIndex, lineIndex));
                }
            }
        }

        return all;
    }

    private static string CultureInvariant(FormattableString text) => FormattableString.Invariant(text);
}
