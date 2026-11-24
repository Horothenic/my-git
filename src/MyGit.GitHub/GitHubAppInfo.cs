namespace MyGit.GitHub;

/// <summary>
/// Identity of the My Git OAuth App registered on GitHub.
/// The client ID is public by design (device flow has no client secret), so it lives in code.
/// </summary>
public static class GitHubAppInfo
{
    /// <summary>
    /// From github.com/settings/developers → OAuth Apps → My Git ("Enable Device Flow" must be on).
    /// </summary>
    public const string ClientId = "Ov23lipaLw782FRVMUMV";

    /// <summary>Sent as the User-Agent on GitHub API calls.</summary>
    public const string ProductName = "MyGit";

    /// <summary>
    /// repo: clone, fetch and push private repositories.
    /// workflow: push changes to files under .github/workflows.
    /// read:user: show your name and avatar.
    /// </summary>
    public static readonly IReadOnlyList<string> Scopes = ["repo", "workflow", "read:user"];

    /// <summary>Where My Git's own releases (Setup.exe and updates) are published.</summary>
    public const string ReleasesRepository = "https://github.com/Horothenic/my-git";

    public static bool IsConfigured => ClientId.Length > 0;
}
