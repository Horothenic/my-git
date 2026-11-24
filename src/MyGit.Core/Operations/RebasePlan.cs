namespace MyGit.Core.Operations;

/// <summary>What an interactive rebase does with one commit.</summary>
public enum RebaseAction
{
    /// <summary>Keep the commit as it is.</summary>
    Pick,

    /// <summary>Keep the commit with a new message.</summary>
    Reword,

    /// <summary>Stop after this commit so it can be amended, then Continue.</summary>
    Edit,

    /// <summary>Join the commit above; both messages are kept.</summary>
    Squash,

    /// <summary>Join the commit above; only the message above is kept.</summary>
    Fixup,

    /// <summary>Leave the commit out.</summary>
    Drop,
}

/// <summary>A commit that an interactive rebase would replay.</summary>
/// <param name="Message">The full message (subject, blank line, body).</param>
public sealed record RebaseCommit(string Sha, string ShortSha, string Subject, string Message);

/// <summary>One line of the plan: a commit, what to do with it, and a new message (for reword / squash).</summary>
/// <param name="NewMessage">Reword: the new message. Squash: this commit's part of the combined message. Otherwise ignored.</param>
public sealed record RebaseStep(RebaseCommit Commit, RebaseAction Action, string? NewMessage = null);
