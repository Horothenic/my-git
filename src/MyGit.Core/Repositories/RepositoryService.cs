using MyGit.Core.Git;

namespace MyGit.Core.Repositories;

/// <summary>
/// Answers basic questions about a repository on disk.
/// </summary>
public sealed class RepositoryService
{
    private readonly IGitRunner _git;

    public RepositoryService(IGitRunner git)
    {
        _git = git;
    }

    /// <summary>
    /// Returns the working tree root that contains <paramref name="path"/>,
    /// or null if the folder is not inside a git repository.
    /// </summary>
    public async Task<string?> FindRootAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(path))
        {
            return null;
        }

        var result = await _git.RunAsync(path, ["rev-parse", "--show-toplevel"], cancellationToken: cancellationToken);

        if (!result.Succeeded)
        {
            return null;
        }

        // git prints forward slashes; GetFullPath normalizes to the Windows style.
        return Path.GetFullPath(result.StandardOutput.Trim());
    }

    /// <summary>
    /// Returns the checked-out branch name, or null when HEAD is detached.
    /// </summary>
    public async Task<string?> GetCurrentBranchAsync(string repositoryPath, CancellationToken cancellationToken = default)
    {
        var result = await _git.RunCheckedAsync(repositoryPath, ["branch", "--show-current"], cancellationToken: cancellationToken);
        var branch = result.StandardOutput.Trim();

        return branch.Length == 0 ? null : branch;
    }
}
