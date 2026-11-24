using System.Globalization;
using MyGit.Core.Git;

namespace MyGit.Core.Operations;

/// <summary>A multi-step git operation that can stop halfway (usually on a conflict).</summary>
public enum OperationKind
{
    None,
    Rebase,
    Merge,
    CherryPick,
    Revert,
}

/// <summary>What git is in the middle of, read from the files git keeps in .git while it runs.</summary>
/// <param name="Branch">For a rebase: the branch being rebased (e.g. "feature/inventory").</param>
/// <param name="Onto">For a rebase: what it's going onto (a branch name when known, else a short id).</param>
/// <param name="Step">For a rebase: which commit it's on (1-based), and how many there are.</param>
/// <param name="CurrentCommit">The commit being replayed / picked / reverted (its message's first line).</param>
/// <param name="IsEditStop">For an interactive rebase: stopped at an "edit" step (not a conflict), so the commit can be changed.</param>
public sealed record OperationState(
    OperationKind Kind,
    string? Branch = null,
    string? Onto = null,
    int Step = 0,
    int TotalSteps = 0,
    string? CurrentCommit = null,
    bool IsEditStop = false)
{
    public static readonly OperationState Idle = new(OperationKind.None);

    public bool IsActive => Kind != OperationKind.None;

    /// <summary>Merges can't skip a commit; the others can (except at an edit stop: that commit is already in).</summary>
    public bool CanSkip => (Kind is OperationKind.Rebase or OperationKind.CherryPick or OperationKind.Revert) && !IsEditStop;
}

public enum ContinueOutcome
{
    /// <summary>Finished (or moved on and finished).</summary>
    Done,

    /// <summary>Moved on, then stopped again on the next conflict.</summary>
    StoppedAgain,
}

/// <summary>
/// Reads and drives a stopped rebase, merge, cherry-pick or revert: continue, skip, abort.
/// </summary>
public sealed class OperationService
{
    // "Continue" makes git open an editor for the commit message; this keeps the message as is.
    private static readonly IReadOnlyDictionary<string, string> NoEditor = new Dictionary<string, string> { ["GIT_EDITOR"] = "true" };

    private readonly IGitRunner _git;

    public OperationService(IGitRunner git)
    {
        _git = git;
    }

    public async Task<OperationState> GetStateAsync(string repositoryPath, CancellationToken cancellationToken = default)
    {
        var gitDir = (await _git.RunAsync(repositoryPath, ["rev-parse", "--absolute-git-dir"], cancellationToken: cancellationToken)).StandardOutput.Trim();
        if (gitDir.Length == 0)
        {
            return OperationState.Idle;
        }

        foreach (var folder in new[] { "rebase-merge", "rebase-apply" })
        {
            var rebase = Path.Combine(gitDir, folder);
            if (Directory.Exists(rebase) && !File.Exists(Path.Combine(rebase, "applying")))
            {
                return await ReadRebaseAsync(repositoryPath, rebase, folder == "rebase-merge", cancellationToken);
            }
        }

        if (File.Exists(Path.Combine(gitDir, "MERGE_HEAD")))
        {
            return new OperationState(OperationKind.Merge, CurrentCommit: ReadFirstLine(Path.Combine(gitDir, "MERGE_MSG")));
        }

        if (File.Exists(Path.Combine(gitDir, "CHERRY_PICK_HEAD")))
        {
            return new OperationState(OperationKind.CherryPick, CurrentCommit: await SubjectAsync(repositoryPath, "CHERRY_PICK_HEAD", cancellationToken));
        }

        if (File.Exists(Path.Combine(gitDir, "REVERT_HEAD")))
        {
            return new OperationState(OperationKind.Revert, CurrentCommit: await SubjectAsync(repositoryPath, "REVERT_HEAD", cancellationToken));
        }

        return OperationState.Idle;
    }

    /// <summary>
    /// Goes on after conflicts are resolved and staged. Returns whether it finished or stopped on the next conflict.
    /// </summary>
    public async Task<ContinueOutcome> ContinueAsync(string repositoryPath, OperationKind kind, CancellationToken cancellationToken = default)
    {
        var result = await _git.RunAsync(repositoryPath, [CommandFor(kind), "--continue"], NoEditor, cancellationToken);
        return await OutcomeAsync(repositoryPath, kind, result, "--continue", cancellationToken);
    }

    /// <summary>Drops the current commit and goes on with the rest (rebase, cherry-pick, revert).</summary>
    public async Task<ContinueOutcome> SkipAsync(string repositoryPath, OperationKind kind, CancellationToken cancellationToken = default)
    {
        var result = await _git.RunAsync(repositoryPath, [CommandFor(kind), "--skip"], NoEditor, cancellationToken);
        return await OutcomeAsync(repositoryPath, kind, result, "--skip", cancellationToken);
    }

    /// <summary>Cancels the whole operation and puts everything back as it was before it started.</summary>
    public Task AbortAsync(string repositoryPath, OperationKind kind, CancellationToken cancellationToken = default) =>
        _git.RunCheckedAsync(repositoryPath, [CommandFor(kind), "--abort"], cancellationToken: cancellationToken);

    private async Task<ContinueOutcome> OutcomeAsync(string repositoryPath, OperationKind kind, GitResult result, string action, CancellationToken cancellationToken)
    {
        if (result.Succeeded)
        {
            return (await GetStateAsync(repositoryPath, cancellationToken)).IsActive ? ContinueOutcome.StoppedAgain : ContinueOutcome.Done;
        }

        // Still going and stopped on a new conflict: that's a normal outcome, not an error.
        var output = result.StandardOutput + result.StandardError;
        if ((await GetStateAsync(repositoryPath, cancellationToken)).IsActive &&
            (output.Contains("CONFLICT", StringComparison.Ordinal) || output.Contains("could not apply", StringComparison.OrdinalIgnoreCase)))
        {
            return ContinueOutcome.StoppedAgain;
        }

        throw new GitCommandException([CommandFor(kind), action], result);
    }

    private async Task<OperationState> ReadRebaseAsync(string repositoryPath, string folder, bool interactiveStyle, CancellationToken cancellationToken)
    {
        var headName = ReadFirstLine(Path.Combine(folder, "head-name"));
        var branch = headName?.StartsWith("refs/heads/", StringComparison.Ordinal) == true ? headName["refs/heads/".Length..] : headName;

        var ontoSha = ReadFirstLine(Path.Combine(folder, "onto"));
        var onto = ontoSha is null ? null : await NameForAsync(repositoryPath, ontoSha, cancellationToken);

        var step = ReadNumber(Path.Combine(folder, interactiveStyle ? "msgnum" : "next"));
        var total = ReadNumber(Path.Combine(folder, interactiveStyle ? "end" : "last"));

        var stopped = File.Exists(Path.Combine(folder, "stopped-sha"))
            ? await SubjectAsync(repositoryPath, ReadFirstLine(Path.Combine(folder, "stopped-sha"))!, cancellationToken)
            : await SubjectAsync(repositoryPath, "REBASE_HEAD", cancellationToken);

        // git writes "amend" when it stops at an edit step: staged changes then go into that commit on continue.
        var isEditStop = File.Exists(Path.Combine(folder, "amend"));

        return new OperationState(OperationKind.Rebase, branch, onto, step, total, stopped, isEditStop);
    }

    /// <summary>A branch name pointing at <paramref name="sha"/> if there is one (e.g. "main"), else a short id.</summary>
    private async Task<string> NameForAsync(string repositoryPath, string sha, CancellationToken cancellationToken)
    {
        var names = await _git.RunAsync(repositoryPath, ["for-each-ref", "--points-at", sha, "--format=%(refname:short)", "refs/heads", "refs/remotes"], cancellationToken: cancellationToken);
        return names.OutputLines.FirstOrDefault(name => !name.EndsWith("/HEAD", StringComparison.Ordinal)) ?? sha[..Math.Min(7, sha.Length)];
    }

    private async Task<string?> SubjectAsync(string repositoryPath, string revision, CancellationToken cancellationToken)
    {
        var result = await _git.RunAsync(repositoryPath, ["log", "-1", "--format=%s", revision, "--"], cancellationToken: cancellationToken);
        return result.Succeeded ? result.StandardOutput.Trim() : null;
    }

    private static string CommandFor(OperationKind kind) => kind switch
    {
        OperationKind.Rebase => "rebase",
        OperationKind.Merge => "merge",
        OperationKind.CherryPick => "cherry-pick",
        OperationKind.Revert => "revert",
        _ => throw new InvalidOperationException("Nothing is in progress."),
    };

    private static string? ReadFirstLine(string path) =>
        File.Exists(path) ? File.ReadLines(path).FirstOrDefault()?.Trim() : null;

    private static int ReadNumber(string path) =>
        int.TryParse(ReadFirstLine(path), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : 0;
}
