namespace MyGit.Core.Git;

/// <summary>
/// Thrown when a git command exits with a non-zero code and the caller asked for success.
/// </summary>
public sealed class GitCommandException : Exception
{
    public GitCommandException(IReadOnlyList<string> arguments, GitResult result)
        : base(BuildMessage(arguments, result))
    {
        Arguments = arguments;
        Result = result;
    }

    public IReadOnlyList<string> Arguments { get; }

    public GitResult Result { get; }

    private static string BuildMessage(IReadOnlyList<string> arguments, GitResult result)
    {
        var details = string.IsNullOrWhiteSpace(result.StandardError)
            ? result.StandardOutput
            : result.StandardError;

        return $"git {string.Join(' ', arguments)} failed with exit code {result.ExitCode}: {details.Trim()}";
    }
}
