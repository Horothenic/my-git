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
    /// Commit hooks run as usual. Returns what was committed, including how to undo it.
    /// </summary>
    public async Task<CommitInfo> CommitAsync(string repositoryPath, string message, bool amend, CancellationToken cancellationToken = default)
    {
        var previousHead = await GetHeadAsync(repositoryPath, cancellationToken);

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

        var details = await _git.RunCheckedAsync(repositoryPath, ["log", "-1", "--format=%H%x00%h%x00%s"], cancellationToken: cancellationToken);
        var parts = details.StandardOutput.Trim().Split('\0');
        var files = await _git.RunCheckedAsync(repositoryPath, ["show", "--name-only", "--format=", "HEAD"], cancellationToken: cancellationToken);

        return new CommitInfo(parts[0], parts[1], parts[2], files.OutputLines.Count, previousHead, amend, DateTimeOffset.Now);
    }

    /// <summary>
    /// Takes back a commit made with <see cref="CommitAsync"/>: the branch goes back to where it was,
    /// and the commit's changes are staged again (nothing in your files changes).
    /// Returns a reason it can't be undone, or null when it was.
    /// </summary>
    public async Task<string?> UndoAsync(string repositoryPath, CommitInfo commit, CancellationToken cancellationToken = default)
    {
        if (await GetHeadAsync(repositoryPath, cancellationToken) != commit.Sha)
        {
            return "Can't undo: the branch has changed since that commit.";
        }

        var remotes = await _git.RunAsync(repositoryPath, ["branch", "-r", "--contains", commit.Sha], cancellationToken: cancellationToken);
        if (remotes.Succeeded && remotes.OutputLines.Count > 0)
        {
            return "Can't undo: that commit is already pushed.";
        }

        if (commit.PreviousHead is { } previous)
        {
            // --soft moves the branch only: files and staged changes stay exactly as they are.
            await _git.RunCheckedAsync(repositoryPath, ["reset", "--soft", previous], cancellationToken: cancellationToken);
        }
        else
        {
            // It was the very first commit: go back to "no commits yet", keeping everything staged.
            await _git.RunCheckedAsync(repositoryPath, ["update-ref", "-d", "HEAD"], cancellationToken: cancellationToken);
        }

        return null;
    }

    /// <summary>The full id of the current commit, or null when there are no commits yet.</summary>
    public async Task<string?> GetHeadAsync(string repositoryPath, CancellationToken cancellationToken = default)
    {
        var result = await _git.RunAsync(repositoryPath, ["rev-parse", "--verify", "-q", "HEAD"], cancellationToken: cancellationToken);
        return result.Succeeded ? result.StandardOutput.Trim() : null;
    }

    /// <summary>The last commit's full message, or null when the repository has no commits yet.</summary>
    public async Task<string?> GetLastMessageAsync(string repositoryPath, CancellationToken cancellationToken = default)
    {
        var result = await _git.RunAsync(repositoryPath, ["log", "-1", "--format=%B"], cancellationToken: cancellationToken);
        return result.Succeeded ? result.StandardOutput.Trim() : null;
    }
}
