using MyGit.Core.Git;

namespace MyGit.Core.Operations;

public enum RebaseOutcome
{
    Done,

    /// <summary>Stopped on a conflict: resolve, then continue (or skip / abort).</summary>
    Conflict,

    /// <summary>Nothing to replay: the branch already contains everything.</summary>
    UpToDate,
}

/// <summary>
/// Rebases the current branch onto another branch or commit.
/// </summary>
public sealed class RebaseService
{
    private readonly IGitRunner _git;

    public RebaseService(IGitRunner git)
    {
        _git = git;
    }

    /// <summary>How many of your commits would be replayed onto <paramref name="onto"/>.</summary>
    public async Task<int> CountCommitsToReplayAsync(string repositoryPath, string onto, CancellationToken cancellationToken = default)
    {
        var result = await _git.RunAsync(repositoryPath, ["rev-list", "--count", $"{onto}..HEAD"], cancellationToken: cancellationToken);
        return result.Succeeded && int.TryParse(result.StandardOutput.Trim(), out var count) ? count : 0;
    }

    /// <summary>
    /// Replays the current branch's commits on top of <paramref name="onto"/>.
    /// With <paramref name="autoStash"/>, uncommitted changes (new files too) are stashed first and
    /// brought back automatically when the rebase finishes or is aborted.
    /// </summary>
    public async Task<RebaseOutcome> RebaseAsync(string repositoryPath, string onto, bool autoStash, CancellationToken cancellationToken = default)
    {
        // git's own --autostash leaves new (untracked) files behind; stash them too so they come along.
        var stashed = false;
        if (autoStash)
        {
            var push = await _git.RunCheckedAsync(
                repositoryPath,
                ["stash", "push", "--include-untracked", "--message", $"My Git: before rebasing onto {onto}"],
                cancellationToken: cancellationToken);
            stashed = !push.StandardOutput.Contains("No local changes", StringComparison.OrdinalIgnoreCase);
        }

        var result = await _git.RunAsync(repositoryPath, ["rebase", onto], new Dictionary<string, string> { ["GIT_EDITOR"] = "true" }, cancellationToken);
        var output = result.StandardOutput + result.StandardError;

        if (!result.Succeeded)
        {
            if (output.Contains("CONFLICT", StringComparison.Ordinal) || output.Contains("could not apply", StringComparison.OrdinalIgnoreCase))
            {
                if (stashed)
                {
                    await RememberStashAsync(repositoryPath, cancellationToken);
                }

                return RebaseOutcome.Conflict;
            }

            if (stashed)
            {
                await _git.RunAsync(repositoryPath, ["stash", "pop"], cancellationToken: cancellationToken);
            }

            throw new GitCommandException(["rebase", onto], result);
        }

        if (stashed)
        {
            await _git.RunAsync(repositoryPath, ["stash", "pop"], cancellationToken: cancellationToken);
        }

        return output.Contains("is up to date", StringComparison.OrdinalIgnoreCase) ? RebaseOutcome.UpToDate : RebaseOutcome.Done;
    }

    /// <summary>
    /// After a rebase that stopped, the stash made before it should come back once the rebase ends
    /// (continued to the end, or aborted). This puts it back if it's still waiting; call it then.
    /// Returns false when the stash couldn't go back cleanly (it then stays in the stash list).
    /// </summary>
    public async Task<bool> RestorePendingStashAsync(string repositoryPath, CancellationToken cancellationToken = default)
    {
        var note = await PendingStashFileAsync(repositoryPath, cancellationToken);
        if (!File.Exists(note))
        {
            return true;
        }

        var sha = (await File.ReadAllTextAsync(note, cancellationToken)).Trim();
        File.Delete(note);

        // Find it by id (its position in the stash list may have changed) and pop it.
        var list = await _git.RunAsync(repositoryPath, ["stash", "list", "--format=%gd %H"], cancellationToken: cancellationToken);
        var entry = list.OutputLines.FirstOrDefault(line => line.EndsWith(sha, StringComparison.Ordinal))?.Split(' ')[0];
        if (entry is null)
        {
            return true; // already gone (applied by hand)
        }

        var pop = await _git.RunAsync(repositoryPath, ["stash", "pop", entry], cancellationToken: cancellationToken);
        return pop.Succeeded;
    }

    /// <summary>True when a rebase started with auto-stash is still holding the stash.</summary>
    public async Task<bool> HasPendingStashAsync(string repositoryPath, CancellationToken cancellationToken = default) =>
        File.Exists(await PendingStashFileAsync(repositoryPath, cancellationToken));

    private async Task RememberStashAsync(string repositoryPath, CancellationToken cancellationToken)
    {
        var top = await _git.RunCheckedAsync(repositoryPath, ["rev-parse", "stash@{0}"], cancellationToken: cancellationToken);
        await File.WriteAllTextAsync(await PendingStashFileAsync(repositoryPath, cancellationToken), top.StandardOutput.Trim(), cancellationToken);
    }

    // A note inside .git (never committed or pushed) instead of the repository's config.
    private async Task<string> PendingStashFileAsync(string repositoryPath, CancellationToken cancellationToken)
    {
        var gitDir = await _git.RunCheckedAsync(repositoryPath, ["rev-parse", "--absolute-git-dir"], cancellationToken: cancellationToken);
        return Path.Combine(gitDir.StandardOutput.Trim(), "MYGIT_PENDING_STASH");
    }
}
