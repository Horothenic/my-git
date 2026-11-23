namespace MyGit.GitHub.Accounts;

/// <summary>
/// The signed-in GitHub user.
/// </summary>
public sealed record GitHubAccount(string Login, string? Name, string AvatarUrl, string ProfileUrl)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Login : Name;
}
