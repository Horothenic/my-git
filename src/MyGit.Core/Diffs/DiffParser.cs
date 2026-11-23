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

        foreach (var rawLine in diff.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');

            if (line.StartsWith("Binary files ", StringComparison.Ordinal) || line.StartsWith("GIT binary patch", StringComparison.Ordinal))
            {
                return FileDiff.Binary(path);
            }

            var hunkHeader = HunkHeader().Match(line);
            if (hunkHeader.Success)
            {
                Flush();
                header = line;
                oldStart = oldLine = int.Parse(hunkHeader.Groups["old"].Value, CultureInfo.InvariantCulture);
                newStart = newLine = int.Parse(hunkHeader.Groups["new"].Value, CultureInfo.InvariantCulture);
                lines = [];
                continue;
            }

            // Lines before the first "@@" are the file header (diff --git, index, ---, +++).
            if (lines is null || line.Length == 0 && rawLine.Length == 0)
            {
                continue;
            }

            switch (line[0])
            {
                case ' ':
                    lines.Add(new DiffLine(DiffLineKind.Context, line[1..], oldLine++, newLine++));
                    break;
                case '-':
                    lines.Add(new DiffLine(DiffLineKind.Removed, line[1..], oldLine++, null));
                    break;
                case '+':
                    lines.Add(new DiffLine(DiffLineKind.Added, line[1..], null, newLine++));
                    break;
                // '\' is "\ No newline at end of file": nothing to show.
            }
        }

        Flush();
        return new FileDiff(path, hunks, IsBinary: false);

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
            return new FileDiff(path, [], IsBinary: false);
        }

        var text = content.Replace("\r\n", "\n").TrimEnd('\n');
        var lines = text.Split('\n')
            .Select((line, index) => new DiffLine(DiffLineKind.Added, line, null, index + 1))
            .ToList();

        return new FileDiff(path, [new DiffHunk($"@@ -0,0 +1,{lines.Count} @@", 0, 1, lines)], IsBinary: false);
    }

    [GeneratedRegex(@"^@@ -(?<old>\d+)(?:,\d+)? \+(?<new>\d+)(?:,\d+)? @@")]
    private static partial Regex HunkHeader();
}
