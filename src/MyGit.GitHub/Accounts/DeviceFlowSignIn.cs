using Octokit;

namespace MyGit.GitHub.Accounts;

/// <summary>
/// A code the user types at <see cref="VerificationUrl"/> to approve My Git.
/// </summary>
public sealed record DeviceCode(string UserCode, string VerificationUrl, DateTimeOffset ExpiresAt, OauthDeviceFlowResponse Response);

/// <summary>
/// GitHub's device flow: get a code, the user approves it in the browser, then exchange it for a token.
/// https://docs.github.com/apps/oauth-apps/building-oauth-apps/authorizing-oauth-apps#device-flow
/// </summary>
public sealed class DeviceFlowSignIn
{
    private readonly GitHubClient _client = new(new ProductHeaderValue(GitHubAppInfo.ProductName));

    /// <summary>Step 1: ask GitHub for a code to show the user.</summary>
    public async Task<DeviceCode> StartAsync(CancellationToken cancellationToken = default)
    {
        if (!GitHubAppInfo.IsConfigured)
        {
            throw new InvalidOperationException(
                "GitHub sign-in isn't set up yet: the OAuth App client ID in GitHubAppInfo.cs is empty.");
        }

        var request = new OauthDeviceFlowRequest(GitHubAppInfo.ClientId);
        foreach (var scope in GitHubAppInfo.Scopes)
        {
            request.Scopes.Add(scope);
        }

        var response = await _client.Oauth.InitiateDeviceFlow(request, cancellationToken);

        return new DeviceCode(
            response.UserCode,
            response.VerificationUri,
            DateTimeOffset.Now.AddSeconds(response.ExpiresIn),
            response);
    }

    /// <summary>
    /// Step 2: wait until the user approves (or denies) in the browser. Returns the access token.
    /// Octokit polls at the interval GitHub asks for.
    /// </summary>
    public async Task<string> WaitForTokenAsync(DeviceCode code, CancellationToken cancellationToken = default)
    {
        var token = await _client.Oauth.CreateAccessTokenForDeviceFlow(GitHubAppInfo.ClientId, code.Response, cancellationToken);

        if (string.IsNullOrEmpty(token.AccessToken))
        {
            throw new InvalidOperationException(token.ErrorDescription ?? token.Error ?? "GitHub didn't return a token.");
        }

        return token.AccessToken;
    }
}
