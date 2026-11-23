using System.Text;
using MyGit.Core.Git;
using MyGit.GitHub.Accounts;

namespace MyGit.Core.Tests.GitHub;

public sealed class AccountManagerTests
{
    [Fact]
    public void GetEnvironment_IsEmpty_WhenSignedOut()
    {
        var manager = new AccountManager(new MemoryTokenStore());

        Assert.False(manager.HasToken);
        Assert.Empty(manager.GetEnvironment());
    }

    [Fact]
    public void GetEnvironment_UsesSavedToken()
    {
        var store = new MemoryTokenStore();
        store.Write("octocat", "gho_example");

        var environment = new AccountManager(store).GetEnvironment();

        Assert.Equal("2", environment["GIT_CONFIG_COUNT"]);
        Assert.Equal("http.https://github.com/.extraheader", environment["GIT_CONFIG_KEY_0"]);

        var expected = Convert.ToBase64String(Encoding.ASCII.GetBytes("x-access-token:gho_example"));
        Assert.Equal($"AUTHORIZATION: basic {expected}", environment["GIT_CONFIG_VALUE_0"]);
    }

    [Fact]
    public void SignOut_DeletesTokenAndClearsEnvironment()
    {
        var store = new MemoryTokenStore();
        store.Write("octocat", "gho_example");
        var manager = new AccountManager(store);
        var changed = false;
        manager.AccountChanged += () => changed = true;

        manager.SignOut();

        Assert.Null(store.Read());
        Assert.Empty(manager.GetEnvironment());
        Assert.True(changed);
    }

    [Fact]
    public async Task GitEnvironment_IsVisibleToGit_ButNotInArguments()
    {
        using var repository = await TempRepository.CreateAsync();
        var runner = new GitRunner(new AccountManager(StoreWith("gho_example")));

        var header = await runner.RunCheckedAsync(repository.Path, ["config", "--get", "http.https://github.com/.extraheader"]);

        Assert.StartsWith("AUTHORIZATION: basic ", header.StandardOutput.Trim());
    }

    private static MemoryTokenStore StoreWith(string token)
    {
        var store = new MemoryTokenStore();
        store.Write("octocat", token);
        return store;
    }

    private sealed class MemoryTokenStore : ITokenStore
    {
        private (string Login, string Token)? _saved;

        public (string Login, string Token)? Read() => _saved;

        public void Write(string login, string token) => _saved = (login, token);

        public void Delete() => _saved = null;
    }
}
