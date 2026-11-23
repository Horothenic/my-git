using System.Globalization;
using MyGit.Core.Git;

namespace MyGit.Core.Undo;

/// <summary>A place the current branch can be moved back to.</summary>
/// <param name="Description">What happened after that point, e.g. "pull with rebase" or "commit “Add slot limit”".</param>
/// <param name="TargetSha">Where the branch was before that happened.</param>
/// <param name="HappenedAt">When it happened.</param>
public sealed record UndoPoint(string Description, string TargetSha, DateTimeOffset HappenedAt);

public enum UndoOutcome
{
    Done,

    /// <summary>Your uncommitted changes would be lost; nothing changed.</summary>
    WouldLoseChanges,
}

/// <summary>
/// Undo for the current branch, built on its reflog: git's record of every commit, pull, rebase
/// or reset that moved the branch. Undoing moves the branch back with "reset --keep", which keeps
/// uncommitted changes and refuses if they'd be overwritten. Undoing an undo works the same way.
/// </summary>
public sealed class UndoService
{
    private readonly IGitRunner _git;

    public UndoService(IGitRunner git)
    {
        _git = git;
    }

    /// <summary>Recent moves of the current branch, newest first (empty when detached or new).</summary>
    public async Task<IReadOnlyList<UndoPoint>> GetPointsAsync(string repositoryPath, int count = 15, CancellationToken cancellationToken = default)
    {
        var branch = (await _git.RunAsync(repositoryPath, ["branch", "--show-current"], cancellationToken: cancellationToken)).StandardOutput.Trim();
        if (branch.Length == 0)
        {
            return [];
        }

        // One extra entry: each point needs the entry before it as its target.
        var result = await _git.RunAsync(
            repositoryPath,
            ["reflog", "show", "-n", (count + 1).ToString(CultureInfo.InvariantCulture), "--format=%H%x00%gs%x00%ct", $"refs/heads/{branch}", "--"],
            cancellationToken: cancellationToken);
        if (!result.Succeeded)
        {
            return [];
        }

        var entries = result.OutputLines.Select(line => line.Split('\0')).Where(fields => fields.Length >= 3).ToList();
        var points = new List<UndoPoint>();

        for (var i = 0; i + 1 < entries.Count; i++)
        {
            points.Add(new UndoPoint(
                Describe(entries[i][1]),
                entries[i + 1][0],
                DateTimeOffset.FromUnixTimeSeconds(long.Parse(entries[i][2], CultureInfo.InvariantCulture)).ToLocalTime()));
        }

        return points;
    }

    /// <summary>
    /// Turns a reflog subject into plain words, e.g. "commit: Add slot limit" → "commit “Add slot limit”",
    /// "rebase (finish): refs/heads/x onto 1a2b" → "rebase".
    /// </summary>
    public static string Describe(string subject)
    {
        var colon = subject.IndexOf(':', StringComparison.Ordinal);
        var action = colon < 0 ? subject : subject[..colon];
        var detail = colon < 0 ? string.Empty : subject[(colon + 1)..].Trim();

        return action switch
        {
            "commit" => $"commit “{detail}”",
            "commit (initial)" => $"first commit “{detail}”",
            "commit (amend)" => $"amend “{detail}”",
            "commit (merge)" => $"merge commit “{detail}”",
            "cherry-pick" => $"cherry-pick “{detail}”",
            "revert" => $"revert “{detail}”",
            "reset" => detail.StartsWith("moving to ", StringComparison.Ordinal) ? $"reset to {Shorten(detail["moving to ".Length..])}" : "reset",
            "merge" or "pull" when detail.Contains("Fast-forward", StringComparison.Ordinal) => $"{action} (fast-forward)",
            "fetch" => "fast-forward from GitHub",
            "branch" => "branch created",
            _ when action.StartsWith("pull --rebase", StringComparison.Ordinal) || (action.StartsWith("rebase", StringComparison.Ordinal) && subject.Contains("pull", StringComparison.Ordinal)) => "pull with rebase",
            _ when action.StartsWith("rebase", StringComparison.Ordinal) => "rebase",
            _ when action.StartsWith("pull", StringComparison.Ordinal) => "pull",
            _ when action.StartsWith("merge", StringComparison.Ordinal) => $"merge {detail}",
            _ => subject,
        };
    }

    /// <summary>True when the current commit is already on the upstream (undoing it makes the next push a force push).</summary>
    public async Task<bool> IsCurrentCommitPushedAsync(string repositoryPath, CancellationToken cancellationToken = default)
    {
        var result = await _git.RunAsync(repositoryPath, ["merge-base", "--is-ancestor", "HEAD", "@{u}"], cancellationToken: cancellationToken);
        return result.Succeeded;
    }

    /// <summary>Moves the current branch back to <paramref name="point"/>, keeping uncommitted changes.</summary>
    public async Task<UndoOutcome> UndoAsync(string repositoryPath, UndoPoint point, CancellationToken cancellationToken = default)
    {
        var result = await _git.RunAsync(repositoryPath, ["reset", "--keep", point.TargetSha], cancellationToken: cancellationToken);

        if (result.Succeeded)
        {
            return UndoOutcome.Done;
        }

        if (result.StandardError.Contains("not uptodate", StringComparison.OrdinalIgnoreCase) ||
            result.StandardError.Contains("would be overwritten", StringComparison.OrdinalIgnoreCase) ||
            result.StandardError.Contains("Cannot do a keep reset", StringComparison.OrdinalIgnoreCase))
        {
            return UndoOutcome.WouldLoseChanges;
        }

        throw new GitCommandException(["reset", "--keep", point.TargetSha], result);
    }

    private static string Shorten(string target) => target.Length == 40 ? target[..7] : target;
}
