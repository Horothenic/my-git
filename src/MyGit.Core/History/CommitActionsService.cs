using MyGit.Core.Git;

namespace MyGit.Core.History;

/// <summary>How far "reset to here" goes.</summary>
public enum ResetMode
{
    /// <summary>Move the branch only; the undone commits' changes wait in Staged.</summary>
    Soft,

    /// <summary>Move the branch and the staging area; the changes wait in Unstaged. git's default.</summary>
    Mixed,

    /// <summary>Move everything: the undone commits and all uncommitted changes are thrown away.</summary>
    Hard,
}

public enum CommitActionOutcome
{
    Done,

    /// <summary>Stopped on a conflict; resolve it, then continue or abort.</summary>
    Conflict,

    /// <summary>Nothing to do: the change is already on this branch.</summary>
    NothingToDo,
}

/// <summary>
/// Cherry-pick, revert and reset, from the History tab's right-click menu.
/// </summary>
public sealed class CommitActionsService
{
    private readonly IGitRunner _git;

    public CommitActionsService(IGitRunner git)
    {
        _git = git;
    }

    /// <summary>Copies a commit onto the current branch as a new commit.</summary>
    public Task<CommitActionOutcome> CherryPickAsync(string repositoryPath, CommitSummary commit, CancellationToken cancellationToken = default) =>
        RunAsync(repositoryPath, ["cherry-pick", .. MainlineFor(commit), commit.Sha], cancellationToken);

    /// <summary>Adds a new commit that undoes <paramref name="commit"/>. Safe for commits that are already pushed.</summary>
    public Task<CommitActionOutcome> RevertAsync(string repositoryPath, CommitSummary commit, CancellationToken cancellationToken = default) =>
        RunAsync(repositoryPath, ["revert", "--no-edit", .. MainlineFor(commit), commit.Sha], cancellationToken);

    /// <summary>Moves the current branch to <paramref name="sha"/>.</summary>
    public Task ResetAsync(string repositoryPath, string sha, ResetMode mode, CancellationToken cancellationToken = default) =>
        _git.RunCheckedAsync(
            repositoryPath,
            ["reset", mode switch { ResetMode.Soft => "--soft", ResetMode.Hard => "--hard", _ => "--mixed" }, sha],
            cancellationToken: cancellationToken);

    /// <summary>How many commits a reset to <paramref name="sha"/> would undo (0 if it's not behind HEAD).</summary>
    public async Task<int> CountCommitsAfterAsync(string repositoryPath, string sha, CancellationToken cancellationToken = default)
    {
        var result = await _git.RunAsync(repositoryPath, ["rev-list", "--count", $"{sha}..HEAD"], cancellationToken: cancellationToken);
        return result.Succeeded && int.TryParse(result.StandardOutput.Trim(), out var count) ? count : 0;
    }

    // For a merge commit, git needs to know which side is "the branch": the first parent.
    private static string[] MainlineFor(CommitSummary commit) => commit.IsMerge ? ["-m", "1"] : [];

    private async Task<CommitActionOutcome> RunAsync(string repositoryPath, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var result = await _git.RunAsync(repositoryPath, arguments, cancellationToken: cancellationToken);
        var output = result.StandardOutput + result.StandardError;

        if (result.Succeeded)
        {
            return CommitActionOutcome.Done;
        }

        if (output.Contains("CONFLICT", StringComparison.Ordinal) || output.Contains("could not apply", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("could not revert", StringComparison.OrdinalIgnoreCase))
        {
            return CommitActionOutcome.Conflict;
        }

        if (output.Contains("nothing to commit", StringComparison.OrdinalIgnoreCase) || output.Contains("is now empty", StringComparison.OrdinalIgnoreCase))
        {
            // Cherry-picking a change that's already here: leave no half-done operation behind.
            await _git.RunAsync(repositoryPath, [arguments[0], "--abort"], cancellationToken: cancellationToken);
            return CommitActionOutcome.NothingToDo;
        }

        throw new GitCommandException(arguments, result);
    }
}
