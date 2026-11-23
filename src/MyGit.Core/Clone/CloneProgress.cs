using System.Text.RegularExpressions;

namespace MyGit.Core.Clone;

/// <summary>
/// One progress update from git clone, for example "Receiving objects", 64%, "41.20 MiB".
/// </summary>
/// <param name="Phase">What git is doing right now.</param>
/// <param name="Percent">0–100 for the current phase, or null when git didn't print one.</param>
/// <param name="Detail">Extra text git printed after the counts, such as the amount received.</param>
public sealed partial record CloneProgress(string Phase, int? Percent, string? Detail)
{
    /// <summary>
    /// Parses one stderr line from "git clone --progress".
    /// Returns null for lines that aren't progress (for example "Cloning into 'x'...").
    /// </summary>
    public static CloneProgress? Parse(string line)
    {
        // Examples:
        //   remote: Counting objects: 100% (512/512), done.
        //   Receiving objects:  64% (1290/2015), 41.20 MiB | 10.30 MiB/s
        //   Resolving deltas:  12% (40/330)
        var match = ProgressLine().Match(line);

        if (!match.Success)
        {
            return null;
        }

        var phase = match.Groups["phase"].Value.Trim();
        var percent = int.Parse(match.Groups["percent"].Value, System.Globalization.CultureInfo.InvariantCulture);
        var detail = match.Groups["detail"].Success ? match.Groups["detail"].Value.Trim().TrimEnd('.') : null;

        if (detail is "done" or "")
        {
            detail = null;
        }

        return new CloneProgress(phase, percent, detail);
    }

    [GeneratedRegex(@"^(?:remote:\s*)?(?<phase>[A-Za-z ]+):\s+(?<percent>\d{1,3})%\s*(?:\([^)]*\))?,?\s*(?<detail>.*)$")]
    private static partial Regex ProgressLine();
}
