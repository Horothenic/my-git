namespace MyGit.Core.Git;

/// <summary>
/// Supplies environment variables that <see cref="GitRunner"/> adds to every git command.
/// Called once per command, so the values can change (for example after signing in or out).
/// </summary>
public interface IGitEnvironmentProvider
{
    IReadOnlyDictionary<string, string> GetEnvironment();
}
