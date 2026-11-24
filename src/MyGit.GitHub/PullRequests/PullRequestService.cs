using MyGit.GitHub.Accounts;
using Octokit;

namespace MyGit.GitHub.PullRequests;

public enum PullRequestState
{
    Open,
    Draft,
    Merged,
    Closed,
}

/// <summary>A pull request, as the toolbar and branch list show it.</summary>
public sealed record PullRequestInfo(
    int Number,
    string Title,
    PullRequestState State,
    string Url,
    string HeadBranch,
    string BaseBranch,
    DateTimeOffset UpdatedAt)
{
    public bool IsActive => State is PullRequestState.Open or PullRequestState.Draft;

    /// <summary>e.g. "#12 open", "#9 merged".</summary>
    public string Badge => $"#{Number} {State.ToString().ToLowerInvariant()}";
}

/// <summary>A GitHub repository by owner and name, e.g. Horothenic/my-git.</summary>
public sealed record GitHubRepositoryId(string Owner, string Name)
{
    /// <summary>From a normalized remote like "github.com/owner/repo" (see RepositoryService.NormalizeRemoteUrl); null for other hosts.</summary>
    public static GitHubRepositoryId? FromNormalizedRemote(string? remote)
    {
        var parts = remote?.Split('/');
        return parts is ["github.com", { Length: > 0 } owner, { Length: > 0 } name] ? new GitHubRepositoryId(owner, name) : null;
    }

    public override string ToString() => $"{Owner}/{Name}";
}

/// <summary>The repository's default branch and its branches (for the PR's base).</summary>
public sealed record RepositoryBranches(string DefaultBranch, IReadOnlyList<string> Branches);

/// <summary>GitHub refused to create the pull request; <see cref="Exception.Message"/> says why, in GitHub's words.</summary>
public sealed class PullRequestException(string message) : Exception(message);

/// <summary>
/// Pull requests on GitHub for the signed-in account: finding a branch's PR, and creating one.
/// </summary>
public sealed class PullRequestService
{
    private readonly AccountManager _accounts;

    public PullRequestService(AccountManager accounts)
    {
        _accounts = accounts;
    }

    public bool IsSignedIn => _accounts.HasToken;

    /// <summary>The 100 most recently updated pull requests opened from branches of this repository (not forks).</summary>
    public async Task<IReadOnlyList<PullRequestInfo>> ListAsync(GitHubRepositoryId repository)
    {
        var client = Client();
        var request = new PullRequestRequest
        {
            State = ItemStateFilter.All,
            SortProperty = PullRequestSort.Updated,
            SortDirection = SortDirection.Descending,
        };

        var pullRequests = await client.PullRequest.GetAllForRepository(
            repository.Owner, repository.Name, request, new ApiOptions { PageSize = 100, PageCount = 1, StartPage = 1 });

        return pullRequests
            .Where(pullRequest => string.Equals(pullRequest.Head.Repository?.FullName, repository.ToString(), StringComparison.OrdinalIgnoreCase))
            .Select(ToInfo)
            .ToList();
    }

    /// <summary>
    /// Each branch's pull request: the open (or draft) one if there is one, else the most recently updated.
    /// </summary>
    public static IReadOnlyDictionary<string, PullRequestInfo> ByBranch(IEnumerable<PullRequestInfo> pullRequests) =>
        pullRequests
            .GroupBy(pullRequest => pullRequest.HeadBranch, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(pullRequest => pullRequest.IsActive).ThenByDescending(pullRequest => pullRequest.UpdatedAt).First(),
                StringComparer.Ordinal);

    public async Task<RepositoryBranches> GetBranchesAsync(GitHubRepositoryId repository)
    {
        var client = Client();
        var details = await client.Repository.Get(repository.Owner, repository.Name);
        var branches = await client.Repository.Branch.GetAll(repository.Owner, repository.Name, new ApiOptions { PageSize = 100, PageCount = 1, StartPage = 1 });

        return new RepositoryBranches(details.DefaultBranch, branches.Select(branch => branch.Name).Order(StringComparer.OrdinalIgnoreCase).ToList());
    }

    /// <summary>Creates the pull request. Throws <see cref="PullRequestException"/> with GitHub's reason when refused.</summary>
    public async Task<PullRequestInfo> CreateAsync(GitHubRepositoryId repository, string head, string @base, string title, string body, bool draft)
    {
        var client = Client();
        try
        {
            var created = await client.PullRequest.Create(repository.Owner, repository.Name, new NewPullRequest(title, head, @base) { Body = body, Draft = draft });
            return ToInfo(created);
        }
        catch (ApiValidationException exception)
        {
            var reason = exception.ApiError?.Errors?.FirstOrDefault(error => !string.IsNullOrWhiteSpace(error.Message))?.Message
                         ?? exception.ApiError?.Message
                         ?? exception.Message;
            throw new PullRequestException(reason);
        }
    }

    private GitHubClient Client() => _accounts.CreateClient() ?? throw new InvalidOperationException("Sign in to GitHub first.");

    private static PullRequestInfo ToInfo(PullRequest pullRequest)
    {
        var state = pullRequest.Merged ? PullRequestState.Merged
            : pullRequest.State.Value == ItemState.Closed ? PullRequestState.Closed
            : pullRequest.Draft ? PullRequestState.Draft
            : PullRequestState.Open;

        return new PullRequestInfo(
            pullRequest.Number,
            pullRequest.Title,
            state,
            pullRequest.HtmlUrl,
            pullRequest.Head.Ref,
            pullRequest.Base.Ref,
            pullRequest.UpdatedAt);
    }
}
