using System.Text;
using MyGit.Core.Git;
using Octokit;

namespace MyGit.GitHub.Accounts;

/// <summary>
/// The one GitHub account My Git uses. Also feeds the token to every git command
/// (as <see cref="IGitEnvironmentProvider"/>) so clone, fetch, pull and push are authenticated.
/// </summary>
public sealed class AccountManager : IGitEnvironmentProvider
{
    private static readonly IReadOnlyDictionary<string, string> NoEnvironment = new Dictionary<string, string>();

    private readonly ITokenStore _tokenStore;
    private string? _token;
    private string? _savedLogin;

    public AccountManager(ITokenStore tokenStore)
    {
        _tokenStore = tokenStore;

        var saved = tokenStore.Read();
        _token = saved?.Token;
        _savedLogin = saved?.Login;
    }

    /// <summary>Raised after signing in or out.</summary>
    public event Action? AccountChanged;

    public GitHubAccount? Account { get; private set; }

    public bool HasToken => _token is not null;

    /// <summary>
    /// Loads the saved account at startup. If the token was revoked on GitHub, signs out.
    /// </summary>
    public async Task RestoreAsync(CancellationToken cancellationToken = default)
    {
        if (_token is null)
        {
            return;
        }

        try
        {
            Account = await LoadAccountAsync(_token);
        }
        catch (AuthorizationException)
        {
            // The token was revoked on GitHub (or the OAuth App was removed).
            SignOut();
            return;
        }
        catch (Exception exception) when (exception is HttpRequestException or ApiException or TaskCanceledException)
        {
            // Offline or GitHub is down: stay signed in with what we saved.
            Account = OfflineAccount(_savedLogin ?? "github");
        }

        AccountChanged?.Invoke();
    }

    /// <summary>Saves a token from <see cref="DeviceFlowSignIn"/> and loads the account.</summary>
    public async Task SignInAsync(string token, CancellationToken cancellationToken = default)
    {
        var account = await LoadAccountAsync(token);

        _tokenStore.Write(account.Login, token);
        _token = token;
        _savedLogin = account.Login;
        Account = account;
        AccountChanged?.Invoke();
    }

    public void SignOut()
    {
        _tokenStore.Delete();
        _token = null;
        _savedLogin = null;
        Account = null;
        AccountChanged?.Invoke();
    }

    /// <summary>An API client for the signed-in user, or null when signed out.</summary>
    public GitHubClient? CreateClient() => _token is null ? null : CreateClient(_token);

    public IReadOnlyDictionary<string, string> GetEnvironment() =>
        _token is null ? NoEnvironment : CreateGitEnvironment(_token);

    /// <summary>
    /// Git settings that authenticate https://github.com requests with the token.
    /// - http.extraheader sends it as a Basic auth header (the same approach as actions/checkout).
    /// - Clearing credential.helper for github.com stops other helpers (like Git Credential Manager)
    ///   from popping up their own sign-in window if the token is ever rejected.
    /// </summary>
    public static IReadOnlyDictionary<string, string> CreateGitEnvironment(string token)
    {
        var basic = Convert.ToBase64String(Encoding.ASCII.GetBytes($"x-access-token:{token}"));

        return GitConfigEnvironment.Create(
        [
            new("http.https://github.com/.extraheader", $"AUTHORIZATION: basic {basic}"),
            new("credential.https://github.com.helper", ""),
        ]);
    }

    private static GitHubAccount OfflineAccount(string login) =>
        new(login, null, $"https://github.com/{login}.png", $"https://github.com/{login}");

    private static GitHubClient CreateClient(string token) =>
        new(new ProductHeaderValue(GitHubAppInfo.ProductName)) { Credentials = new Credentials(token) };

    private static async Task<GitHubAccount> LoadAccountAsync(string token)
    {
        var user = await CreateClient(token).User.Current();
        return new GitHubAccount(user.Login, user.Name, user.AvatarUrl, user.HtmlUrl);
    }
}
