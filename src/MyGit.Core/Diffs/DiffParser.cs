using System.Globalization;
using System.Text.RegularExpressions;

namespace MyGit.Core.Diffs;

/// <summary>
/// Parses the unified diff git prints for one file.
/// </summary>
public static partial class DiffParser
{
    public static FileDiff Parse(string path, string diff)
    {
        var hunks = new List<DiffHunk>();
        List<DiffLine>? lines = null;
        string header = string.Empty;
        int oldStart = 0, newStart = 0, oldLine = 0, newLine = 0;

        // Split on '\n' only: a '\r' before it belongs to the line (Windows line endings)
        // and must survive into patches.
        var rawLines = diff.Split('\n');

        // The text ends with "\n", which leaves one empty item after the last line.
        var count = rawLines.Length > 0 && rawLines[^1].Length == 0 ? rawLines.Length - 1 : rawLines.Length;

        for (var index = 0; index < count; index++)
        {
            var raw = rawLines[index];

            if (raw.StartsWith("Binary files ", StringComparison.Ordinal) || raw.StartsWith("GIT binary patch", StringComparison.Ordinal))
            {
                return FileDiff.Binary(path) with { RawText = diff };
            }

            var hunkHeader = HunkHeader().Match(raw);
            if (hunkHeader.Success)
            {
                Flush();
                header = raw.TrimEnd('\r');
                oldStart = oldLine = int.Parse(hunkHeader.Groups["old"].Value, CultureInfo.InvariantCulture);
                newStart = newLine = int.Parse(hunkHeader.Groups["new"].Value, CultureInfo.InvariantCulture);
                lines = [];
                continue;
            }

            // Lines before the first "@@" are the file header (diff --git, index, ---, +++).
            if (lines is null || raw.Length == 0)
            {
                continue;
            }

            var content = raw[1..];
            var text = content.TrimEnd('\r');

            switch (raw[0])
            {
                case ' ':
                    lines.Add(new DiffLine(DiffLineKind.Context, text, oldLine++, newLine++, content));
                    break;
                case '-':
                    lines.Add(new DiffLine(DiffLineKind.Removed, text, oldLine++, null, content));
                    break;
                case '+':
                    lines.Add(new DiffLine(DiffLineKind.Added, text, null, newLine++, content));
                    break;
                case '\\' when lines.Count > 0:
                    // "\ No newline at end of file" belongs to the line before it.
                    lines[^1] = lines[^1] with { NoNewlineAtEnd = true };
                    break;
            }
        }

        Flush();
        return new FileDiff(path, hunks, IsBinary: false, RawText: diff);

        void Flush()
        {
            if (lines is { Count: > 0 })
            {
                hunks.Add(new DiffHunk(header, oldStart, newStart, lines));
            }
        }
    }

    /// <summary>
    /// A diff for a new, untracked file: every line is added.
    /// </summary>
    public static FileDiff ForNewFile(string path, string content)
    {
        if (content.Length == 0)
        {
            return new FileDiff(path, [], IsBinary: false, RawText: content);
        }

        var text = content.Replace("\r\n", "\n").TrimEnd('\n');
        var lines = text.Split('\n')
            .Select((line, index) => new DiffLine(DiffLineKind.Added, line, null, index + 1))
            .ToList();

        return new FileDiff(path, [new DiffHunk($"@@ -0,0 +1,{lines.Count} @@", 0, 1, lines)], IsBinary: false, RawText: content);
    }

    [GeneratedRegex(@"^@@ -(?<old>\d+)(?:,\d+)? \+(?<new>\d+)(?:,\d+)? @@")]
    private static partial Regex HunkHeader();
}
