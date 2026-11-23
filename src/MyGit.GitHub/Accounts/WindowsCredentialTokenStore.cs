using System.Runtime.Versioning;
using Meziantou.Framework.Win32;

namespace MyGit.GitHub.Accounts;

/// <summary>
/// Stores the token in Windows Credential Manager (Control Panel → Credential Manager →
/// Windows Credentials → "MyGit:github.com"). It's encrypted for your Windows user only.
/// </summary>
[SupportedOSPlatform("windows5.1.2600")]
public sealed class WindowsCredentialTokenStore : ITokenStore
{
    private readonly string _targetName;

    public WindowsCredentialTokenStore(string targetName = "MyGit:github.com")
    {
        _targetName = targetName;
    }

    public (string Login, string Token)? Read()
    {
        var credential = CredentialManager.ReadCredential(_targetName);

        if (credential?.UserName is null || string.IsNullOrEmpty(credential.Password))
        {
            return null;
        }

        return (credential.UserName, credential.Password);
    }

    public void Write(string login, string token) =>
        CredentialManager.WriteCredential(
            _targetName,
            login,
            token,
            "GitHub token used by My Git",
            CredentialPersistence.LocalMachine);

    public void Delete() => CredentialManager.TryDeleteCredential(_targetName);
}
