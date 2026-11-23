using System.Globalization;
using MyGit.Core.History;

namespace MyGit.App.ViewModels.History;

/// <summary>A branch, tag or HEAD label on a commit row.</summary>
public sealed class RefLabelViewModel(RefLabel label)
{
    public string Name { get; } = label.Name;

    public RefKind Kind { get; } = label.Kind;

    public bool IsHead => Kind == RefKind.Head;

    public bool IsLocal => Kind == RefKind.LocalBranch;

    public bool IsRemote => Kind == RefKind.RemoteBranch;

    public bool IsTag => Kind == RefKind.Tag;
}

/// <summary>One row of the History list: graph, labels, message, author, date.</summary>
public sealed class CommitRowViewModel
{
    public CommitRowViewModel(CommitSummary commit, GraphRow graph)
    {
        Commit = commit;
        Graph = graph;
        Refs = commit.Refs.Select(label => new RefLabelViewModel(label)).ToList();
        DateText = Describe(commit.AuthoredAt);
        DateToolTip = commit.AuthoredAt.ToString("f", CultureInfo.CurrentCulture);
    }

    public CommitSummary Commit { get; }

    public GraphRow Graph { get; }

    public string Sha => Commit.Sha;

    public string ShortSha => Commit.ShortSha;

    public string Subject => Commit.Subject;

    public string Author => Commit.Author;

    public IReadOnlyList<RefLabelViewModel> Refs { get; }

    public bool HasRefs => Refs.Count > 0;

    /// <summary>The first tag on this commit, for "Create branch from tag…".</summary>
    public string? FirstTag => Commit.Refs.FirstOrDefault(label => label.Kind == RefKind.Tag)?.Name;

    public bool HasTag => FirstTag is not null;

    public string DateText { get; }

    public string DateToolTip { get; }

    private static string Describe(DateTimeOffset when)
    {
        var age = DateTimeOffset.Now - when;
        return age switch
        {
            { TotalMinutes: < 1 } => "just now",
            { TotalMinutes: < 60 } => $"{(int)age.TotalMinutes} min ago",
            { TotalHours: < 24 } => $"{(int)age.TotalHours} h ago",
            { TotalDays: < 2 } => "yesterday",
            { TotalDays: < 7 } => $"{(int)age.TotalDays} days ago",
            _ => when.ToString("MMM d, yyyy", CultureInfo.CurrentCulture),
        };
    }
}
