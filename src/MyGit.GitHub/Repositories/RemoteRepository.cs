namespace MyGit.GitHub.Repositories;

/// <summary>
/// A repository on GitHub that the signed-in user can access.
/// </summary>
public sealed record RemoteRepository(
    string Owner,
    string Name,
    bool IsPrivate,
    string CloneUrl,
    DateTimeOffset? PushedAt);
