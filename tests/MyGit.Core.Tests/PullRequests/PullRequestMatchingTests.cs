using MyGit.GitHub.PullRequests;

namespace MyGit.Core.Tests.PullRequests;

public sealed class PullRequestMatchingTests
{
    private static PullRequestInfo Pr(int number, string branch, PullRequestState state, int daysAgo) =>
        new(number, $"PR {number}", state, $"https://github.com/o/r/pull/{number}", branch, "main", DateTimeOffset.UtcNow.AddDays(-daysAgo));

    [Fact]
    public void ByBranch_PrefersTheOpenPullRequest_ThenTheLatest()
    {
        var byBranch = PullRequestService.ByBranch(
        [
            Pr(3, "feature", PullRequestState.Closed, daysAgo: 0),
            Pr(5, "feature", PullRequestState.Open, daysAgo: 4),
            Pr(7, "fix", PullRequestState.Merged, daysAgo: 1),
            Pr(2, "fix", PullRequestState.Closed, daysAgo: 9),
        ]);

        Assert.Equal(5, byBranch["feature"].Number);
        Assert.Equal(7, byBranch["fix"].Number);
        Assert.Equal("#7 merged", byBranch["fix"].Badge);
    }

    [Theory]
    [InlineData("github.com/Horothenic/my-git", "Horothenic", "my-git")]
    [InlineData("gitlab.com/someone/thing", null, null)]
    [InlineData(null, null, null)]
    public void RepositoryId_ComesFromTheNormalizedRemote(string? remote, string? owner, string? name)
    {
        var id = GitHubRepositoryId.FromNormalizedRemote(remote);

        Assert.Equal(owner, id?.Owner);
        Assert.Equal(name, id?.Name);
    }
}
