using MyGit.Core.Changes;

namespace MyGit.Core.Tests.Changes;

public sealed class HiddenFilesStoreTests : IDisposable
{
    private const string Repo = @"C:\code\game";
    private readonly string _folder = TempRepository.CreateEmptyFolder();

    private string FilePath => Path.Combine(_folder, "hidden.json");

    [Fact]
    public void Hide_FileAndFolder_PersistAcrossInstances()
    {
        new HiddenFilesStore(FilePath).Hide(Repo, ["ProjectSettings/", "Packages/packages-lock.json"]);

        var store = new HiddenFilesStore(FilePath);

        Assert.True(store.IsHidden(Repo, "ProjectSettings/TagManager.asset"));
        Assert.True(store.IsHidden(Repo, "ProjectSettings/Nested/New.asset")); // future files in the folder too
        Assert.True(store.IsHidden(Repo, "Packages/packages-lock.json"));
        Assert.False(store.IsHidden(Repo, "Packages/manifest.json"));
        Assert.False(store.IsHidden(Repo, "ProjectSettingsBackup.asset"));
        Assert.False(store.IsHidden(@"C:\code\other", "ProjectSettings/TagManager.asset"));
    }

    [Fact]
    public void Unhide_FileInsideHiddenFolder_UnhidesTheFolder()
    {
        var store = new HiddenFilesStore(FilePath);
        store.Hide(Repo, ["ProjectSettings/"]);

        store.Unhide(Repo, ["ProjectSettings/TagManager.asset"]);

        Assert.False(store.IsHidden(Repo, "ProjectSettings/Other.asset"));
        Assert.Empty(store.GetEntries(Repo));
    }

    [Fact]
    public void Unhide_Folder_RemovesFilesHiddenInsideIt()
    {
        var store = new HiddenFilesStore(FilePath);
        store.Hide(Repo, ["Assets/Generated/a.cs", "Assets/Generated/b.cs", "README.md"]);

        store.Unhide(Repo, ["Assets/Generated/"]);

        Assert.Equal(["README.md"], store.GetEntries(Repo));
    }

    [Theory]
    [InlineData(@"C:\code\game", "https://github.com/Horothenic/Game.git", "github.com/horothenic/game")]
    [InlineData(@"D:\moved\game-copy", "git@github.com:Horothenic/Game.git", "github.com/horothenic/game")]
    [InlineData(@"C:\code\local-only", null, @"C:\code\local-only")]
    public void KeyFor_UsesGitHubAddress_OrFolderWithoutRemote(string folder, string? remote, string expected)
    {
        Assert.Equal(expected, HiddenFilesStore.KeyFor(folder, remote));
    }

    [Fact]
    public void MoveKey_CarriesFolderEntriesToTheAddress()
    {
        var store = new HiddenFilesStore(FilePath);
        store.Hide(Repo, ["ProjectSettings/"]);
        store.Hide("github.com/horothenic/game", ["README.md"]);

        store.MoveKey(Repo, "github.com/horothenic/game");

        var reloaded = new HiddenFilesStore(FilePath);
        Assert.Empty(reloaded.GetEntries(Repo));
        Assert.Equal(["ProjectSettings/", "README.md"], reloaded.GetEntries("github.com/horothenic/game"));
    }

    [Fact]
    public void IsHidden_IgnoresCase()
    {
        var store = new HiddenFilesStore(FilePath);
        store.Hide(Repo, ["Library/"]);

        Assert.True(store.IsHidden(@"c:\CODE\game", "library/x.asset"));
    }

    public void Dispose() => TempRepository.DeleteFolder(_folder);
}
