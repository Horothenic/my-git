namespace MyGit.Core.Sync;

/// <summary>How "Pull" combines GitHub's new commits with yours.</summary>
public enum PullMode
{
    /// <summary>Replay your commits on top of GitHub's (git pull --rebase). The default.</summary>
    Rebase,

    /// <summary>Join both with a merge commit (git pull --no-rebase).</summary>
    Merge,

    /// <summary>Only move forward when nothing diverged (git pull --ff-only).</summary>
    FastForwardOnly,
}

/// <summary>
/// Where the current branch stands compared to its GitHub copy, from the last fetch.
/// </summary>
/// <param name="Branch">Checked-out branch, or null when HEAD is detached.</param>
/// <param name="Upstream">The branch it tracks, e.g. "origin/main", or null when it isn't published.</param>
/// <param name="Ahead">Commits you have that the upstream doesn't (to push).</param>
/// <param name="Behind">Commits the upstream has that you don't (to pull).</param>
/// <param name="HasRemote">True when the repository has at least one remote (usually "origin").</param>
public sealed record TrackingInfo(string? Branch, string? Upstream, int Ahead, int Behind, bool HasRemote)
{
    public static readonly TrackingInfo None = new(null, null, 0, 0, false);

    public bool IsDetached => Branch is null;

    /// <summary>A branch that exists only here: Push becomes Publish.</summary>
    public bool NeedsPublish => Branch is not null && Upstream is null && HasRemote;
}

public enum PullOutcome
{
    /// <summary>Done (possibly nothing new).</summary>
    Done,

    /// <summary>The pull worked, but putting your stashed changes back conflicted; they're still in the stash.</summary>
    StashConflict,

    /// <summary>The rebase or merge stopped on a conflict and needs resolving.</summary>
    Conflict,

    /// <summary>Fast-forward only, but the branches diverged.</summary>
    NotFastForward,

    /// <summary>There's nothing to pull from (no upstream).</summary>
    NoUpstream,
}

public sealed record PullResult(PullOutcome Outcome, int CommitsPulled, string Upstream, string? Details = null);

public enum PushOutcome
{
    Done,

    /// <summary>GitHub has commits you don't: pull first (or force push).</summary>
    Rejected,

    /// <summary>A force push was refused because GitHub changed since your last fetch.</summary>
    LeaseRejected,
}

public sealed record PushResult(PushOutcome Outcome, int CommitsPushed, string Upstream, bool Published = false);
