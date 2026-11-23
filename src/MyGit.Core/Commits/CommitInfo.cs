namespace MyGit.Core.Commits;

/// <summary>
/// A commit just made from the commit box, with what's needed to undo it.
/// </summary>
/// <param name="Sha">Full commit id.</param>
/// <param name="ShortSha">Short id shown to the user, e.g. "3786c42".</param>
/// <param name="Summary">First line of the message.</param>
/// <param name="FileCount">How many files the commit touched.</param>
/// <param name="PreviousHead">Where the branch pointed before (null for a repository's first commit).</param>
/// <param name="WasAmend">True when it replaced the previous commit.</param>
/// <param name="CommittedAt">When it was made.</param>
public sealed record CommitInfo(
    string Sha,
    string ShortSha,
    string Summary,
    int FileCount,
    string? PreviousHead,
    bool WasAmend,
    DateTimeOffset CommittedAt);
