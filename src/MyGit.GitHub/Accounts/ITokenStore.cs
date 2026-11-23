namespace MyGit.GitHub.Accounts;

/// <summary>
/// Keeps the GitHub token between app runs.
/// </summary>
public interface ITokenStore
{
    /// <summary>Returns the saved login and token, or null when nobody is signed in.</summary>
    (string Login, string Token)? Read();

    void Write(string login, string token);

    void Delete();
}
