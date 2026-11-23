using MyGit.Core.Changes;

namespace MyGit.Core.History;

public enum RefKind
{
    /// <summary>Where you are now ("HEAD").</summary>
    Head,

    /// <summary>A local branch, e.g. "main".</summary>
    LocalBranch,

    /// <summary>A remote-tracking branch, e.g. "origin/main".</summary>
    RemoteBranch,

    /// <summary>A tag, e.g. "v1.2.0".</summary>
    Tag,
}

/// <summary>A label shown on a commit in the history: a branch, a tag, or HEAD.</summary>
public sealed record RefLabel(RefKind Kind, string Name);

/// <summary>One commit in the history list.</summary>
/// <param name="Sha">Full commit id.</param>
/// <param name="Parents">Parent ids: none for the first commit, two or more for merges.</param>
/// <param name="AuthoredAt">When the author made the commit.</param>
public sealed record CommitSummary(
    string Sha,
    IReadOnlyList<string> Parents,
    string Author,
    DateTimeOffset AuthoredAt,
    string Subject,
    IReadOnlyList<RefLabel> Refs)
{
    public string ShortSha => Sha[..Math.Min(7, Sha.Length)];

    public bool IsMerge => Parents.Count > 1;
}

/// <summary>Everything shown for the selected commit.</summary>
public sealed record CommitDetails(
    string Sha,
    IReadOnlyList<string> Parents,
    string Author,
    string AuthorEmail,
    DateTimeOffset AuthoredAt,
    string Message,
    IReadOnlyList<FileChange> Files);
