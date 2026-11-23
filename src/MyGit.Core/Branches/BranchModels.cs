namespace MyGit.Core.Branches;

/// <summary>A branch in this repository.</summary>
/// <param name="Name">e.g. "feature/inventory".</param>
/// <param name="Upstream">The remote branch it tracks, e.g. "origin/feature/inventory", or null if not published.</param>
/// <param name="Ahead">Commits here that the upstream doesn't have (from the last fetch).</param>
/// <param name="Behind">Commits on the upstream that aren't here (from the last fetch).</param>
/// <param name="UpstreamGone">True when the upstream was deleted on GitHub (e.g. after its PR was merged).</param>
public sealed record LocalBranch(string Name, string? Upstream, int Ahead, int Behind, bool IsCurrent, bool UpstreamGone)
{
    /// <summary>Can be updated without switching to it: tracks something, is behind, and has nothing of its own.</summary>
    public bool CanFastForward => !IsCurrent && Upstream is not null && !UpstreamGone && Behind > 0 && Ahead == 0;
}

/// <summary>A branch on a remote that has no local branch tracking it yet.</summary>
/// <param name="Name">e.g. "origin/fix-login".</param>
/// <param name="LocalName">The local name it would get, e.g. "fix-login".</param>
public sealed record RemoteBranch(string Name, string Remote, string LocalName);

public sealed record BranchList(IReadOnlyList<LocalBranch> Local, IReadOnlyList<RemoteBranch> Remote);

public enum SwitchOutcome
{
    Done,

    /// <summary>Uncommitted changes would be overwritten: offer to stash them.</summary>
    NeedsStash,

    /// <summary>Switched, but putting the stashed changes back conflicted; they're still in the stash.</summary>
    StashConflict,
}

public enum DeleteOutcome
{
    Done,

    /// <summary>The branch has commits that aren't merged anywhere: ask before deleting them.</summary>
    NotMerged,
}

public enum FastForwardOutcome
{
    Done,

    /// <summary>The branch has its own commits, so it can't just move forward.</summary>
    Diverged,
}
