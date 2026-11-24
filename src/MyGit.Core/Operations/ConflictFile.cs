namespace MyGit.Core.Operations;

/// <summary>How one conflict in a file is resolved.</summary>
public enum ConflictChoice
{
    /// <summary>Not decided yet: the result keeps git's conflict markers for it.</summary>
    Unresolved,

    /// <summary>Keep git's "ours" side.</summary>
    Ours,

    /// <summary>Keep git's "theirs" side.</summary>
    Theirs,

    /// <summary>Keep both: ours first, then theirs.</summary>
    Both,
}

/// <summary>A piece of a conflicted file: unchanged lines, or one conflict.</summary>
public abstract record ConflictSegment;

/// <summary>Lines both sides agree on.</summary>
public sealed record CommonSegment(IReadOnlyList<string> Lines) : ConflictSegment;

/// <summary>One conflict: what each side has here (and the common ancestor, if git wrote it).</summary>
/// <param name="OursLabel">Text after "&lt;&lt;&lt;&lt;&lt;&lt;&lt;", e.g. "HEAD".</param>
/// <param name="Base">Lines from the common ancestor (only with merge.conflictStyle=diff3), else null.</param>
/// <param name="TheirsLabel">Text after "&gt;&gt;&gt;&gt;&gt;&gt;&gt;", e.g. "a1b2c3d (Add slot limit)".</param>
public sealed record ConflictBlock(
    string OursLabel,
    IReadOnlyList<string> Ours,
    IReadOnlyList<string>? Base,
    string? BaseLabel,
    IReadOnlyList<string> Theirs,
    string TheirsLabel) : ConflictSegment;

/// <summary>
/// A conflicted text file split into common parts and conflicts, so each conflict can be decided
/// on its own and the file rebuilt from those decisions.
/// </summary>
public sealed class ConflictFile
{
    private ConflictFile(IReadOnlyList<ConflictSegment> segments, string newline, bool endsWithNewline)
    {
        Segments = segments;
        Newline = newline;
        EndsWithNewline = endsWithNewline;
        Conflicts = segments.OfType<ConflictBlock>().ToList();
    }

    public IReadOnlyList<ConflictSegment> Segments { get; }

    public IReadOnlyList<ConflictBlock> Conflicts { get; }

    /// <summary>"\r\n" or "\n", as the file uses, so the result keeps its line endings.</summary>
    public string Newline { get; }

    public bool EndsWithNewline { get; }

    /// <summary>
    /// Splits text with git's conflict markers. Returns null when the text has no (complete) conflicts,
    /// e.g. a file deleted on one side, or markers that don't pair up.
    /// </summary>
    public static ConflictFile? Parse(string text)
    {
        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var endsWithNewline = text.EndsWith('\n');
        var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
        if (endsWithNewline)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        var segments = new List<ConflictSegment>();
        var common = new List<string>();
        var i = 0;

        while (i < lines.Count)
        {
            if (!IsMarker(lines[i], '<'))
            {
                common.Add(lines[i++]);
                continue;
            }

            if (common.Count > 0)
            {
                segments.Add(new CommonSegment(common.ToList()));
                common.Clear();
            }

            var oursLabel = Label(lines[i++]);
            var ours = new List<string>();
            List<string>? @base = null;
            string? baseLabel = null;
            var theirs = new List<string>();

            while (i < lines.Count && !IsMarker(lines[i], '|') && !IsMarker(lines[i], '='))
            {
                ours.Add(lines[i++]);
            }

            if (i < lines.Count && IsMarker(lines[i], '|'))
            {
                baseLabel = Label(lines[i++]);
                @base = [];
                while (i < lines.Count && !IsMarker(lines[i], '='))
                {
                    @base.Add(lines[i++]);
                }
            }

            if (i >= lines.Count)
            {
                return null; // no "=======": not a real conflict
            }

            i++; // skip =======

            while (i < lines.Count && !IsMarker(lines[i], '>'))
            {
                theirs.Add(lines[i++]);
            }

            if (i >= lines.Count)
            {
                return null; // no ">>>>>>>"
            }

            var theirsLabel = Label(lines[i++]);
            segments.Add(new ConflictBlock(oursLabel, ours, @base, baseLabel, theirs, theirsLabel));
        }

        if (common.Count > 0)
        {
            segments.Add(new CommonSegment(common));
        }

        return segments.Any(segment => segment is ConflictBlock) ? new ConflictFile(segments, newline, endsWithNewline) : null;
    }

    /// <summary>
    /// The file with each conflict replaced by its choice (one choice per conflict, in order).
    /// Unresolved conflicts keep their markers.
    /// </summary>
    public string Build(IReadOnlyList<ConflictChoice> choices)
    {
        if (choices.Count != Conflicts.Count)
        {
            throw new ArgumentException($"Expected {Conflicts.Count} choices, got {choices.Count}.", nameof(choices));
        }

        var output = new List<string>();
        var conflictIndex = 0;

        foreach (var segment in Segments)
        {
            if (segment is CommonSegment common)
            {
                output.AddRange(common.Lines);
                continue;
            }

            var conflict = (ConflictBlock)segment;
            switch (choices[conflictIndex++])
            {
                case ConflictChoice.Ours:
                    output.AddRange(conflict.Ours);
                    break;
                case ConflictChoice.Theirs:
                    output.AddRange(conflict.Theirs);
                    break;
                case ConflictChoice.Both:
                    output.AddRange(conflict.Ours);
                    output.AddRange(conflict.Theirs);
                    break;
                default:
                    output.Add(Marker('<', conflict.OursLabel));
                    output.AddRange(conflict.Ours);
                    if (conflict.Base is { } @base)
                    {
                        output.Add(Marker('|', conflict.BaseLabel ?? string.Empty));
                        output.AddRange(@base);
                    }

                    output.Add(new string('=', 7));
                    output.AddRange(conflict.Theirs);
                    output.Add(Marker('>', conflict.TheirsLabel));
                    break;
            }
        }

        var text = string.Join(Newline, output);
        return EndsWithNewline && output.Count > 0 ? text + Newline : text;
    }

    // A marker is 7 of the same character, alone or followed by a space and a label.
    private static bool IsMarker(string line, char character) =>
        line.Length >= 7 && line.AsSpan(0, 7).IndexOfAnyExcept(character) < 0 && (line.Length == 7 || line[7] == ' ');

    private static string Label(string line) => line.Length > 8 ? line[8..] : string.Empty;

    private static string Marker(char character, string label) =>
        label.Length == 0 ? new string(character, 7) : $"{new string(character, 7)} {label}";
}
