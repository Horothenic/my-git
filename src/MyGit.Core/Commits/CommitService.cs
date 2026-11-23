using MyGit.Core.Git;

namespace MyGit.Core.Commits;

/// <summary>
/// Creates commits and amends the last one.
/// </summary>
public sealed class CommitService
{
    private readonly IGitRunner _git;

    public CommitService(IGitRunner git)
    {
        _git = git;
    }

    /// <summary>
    /// Joins the summary and description the way git expects: summary, blank line, description.
    /// </summary>
    public static string BuildMessage(string summary, string? description)
    {
        var body = description?.Trim();
        return string.IsNullOrEmpty(body) ? summary.Trim() : $"{summary.Trim()}\n\n{body}";
    }

    /// <summary>Splits a commit message into its summary (first line) and description (the rest).</summary>
    public static (string Summary, string Description) SplitMessage(string message)
    {
        var normalized = message.Replace("\r\n", "\n").Trim();
        var firstBreak = normalized.IndexOf('\n');

        return firstBreak < 0
            ? (normalized, string.Empty)
            : (normalized[..firstBreak].Trim(), normalized[(firstBreak + 1)..].Trim());
    }

    /// <summary>
    /// Commits what's staged. With <paramref name="amend"/>, replaces the last commit instead
    /// (its changes plus whatever is staged now, with the new message).
    /// Commit hooks run as usual.
    /// </summary>
    public async Task CommitAsync(string repositoryPath, string message, bool amend, CancellationToken cancellationToken = default)
    {
        // The message goes through a file so line breaks and special characters survive exactly.
        var messageFile = Path.GetTempFileName();

        try
        {
            await File.WriteAllTextAsync(messageFile, message, cancellationToken);

            List<string> arguments = ["commit", "--file", messageFile, "--cleanup=strip"];
            if (amend)
            {
                arguments.Add("--amend");
            }

            await _git.RunCheckedAsync(repositoryPath, arguments, cancellationToken: cancellationToken);
        }
        finally
        {
            File.Delete(messageFile);
        }
    }

    /// <summary>The last commit's full message, or null when the repository has no commits yet.</summary>
    public async Task<string?> GetLastMessageAsync(string repositoryPath, CancellationToken cancellationToken = default)
    {
        var result = await _git.RunAsync(repositoryPath, ["log", "-1", "--format=%B"], cancellationToken: cancellationToken);
        return result.Succeeded ? result.StandardOutput.Trim() : null;
    }
}
