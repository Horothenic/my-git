using MyGit.GitHub.Accounts;
using Octokit;

namespace MyGit.GitHub.Repositories;

/// <summary>
/// Lists the repositories the signed-in user can clone.
/// </summary>
public sealed class GitHubRepositoryService
{
    private readonly AccountManager _accounts;

    public GitHubRepositoryService(AccountManager accounts)
    {
        _accounts = accounts;
    }

    /// <summary>
    /// Your own repositories, then those of organizations and collaborations,
    /// most recently pushed first. Empty when signed out.
    /// </summary>
    public async Task<IReadOnlyList<RemoteRepository>> ListAsync()
    {
        var client = _accounts.CreateClient();

        if (client is null)
        {
            return [];
        }

        var request = new RepositoryRequest
        {
            Affiliation = RepositoryAffiliation.All,
            Sort = RepositorySort.Pushed,
            Direction = SortDirection.Descending,
        };

        var repositories = await client.Repository.GetAllForCurrent(request, new ApiOptions { PageSize = 100 });

        return repositories
            .Where(repository => !repository.Archived)
            .Select(repository => new RemoteRepository(
                repository.Owner.Login,
                repository.Name,
                repository.Private,
                repository.CloneUrl,
                repository.PushedAt))
            .ToList();
    }
}
