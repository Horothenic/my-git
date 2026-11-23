namespace MyGit.Core.Git;

/// <summary>
/// Runs the git command line. Every write operation in My Git goes through here.
/// </summary>
public interface IGitRunner
{
    /// <summary>Runs git and returns the result, whatever the exit code.</summary>
    /// <param name="onProgress">
    /// If set, called with each line git writes to standard error while it runs
    /// (git prints progress there, for example during clone and fetch).
    /// </param>
    Task<GitResult> RunAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string>? environment = null,
        CancellationToken cancellationToken = default,
        Action<string>? onProgress = null);

    /// <summary>Runs git and throws <see cref="GitCommandException"/> if it fails.</summary>
    Task<GitResult> RunCheckedAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string>? environment = null,
        CancellationToken cancellationToken = default,
        Action<string>? onProgress = null);
}
