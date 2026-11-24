using MyGit.Core.Repositories;

namespace MyGit.Core.Tests.Repositories;

public sealed class RepositoryStoreTests : IDisposable
{
    private readonly string _folder = TempRepository.CreateEmptyFolder();

    private string FilePath => Path.Combine(_folder, "settings", "repositories.json");

    // A folder under <drive root>\code on Windows or /code on a Mac; never created.
    private static string Code(string name) =>
        Path.Combine(Path.GetPathRoot(Path.GetTempPath())!, "code", name);

    [Fact]
    public void Add_PersistsAcrossInstances()
    {
        var store = new RepositoryStore(FilePath);
        store.Add(RepositoryInfo.FromPath(Code("beta")));
        store.Add(RepositoryInfo.FromPath(Code("alpha")));

        var reloaded = new RepositoryStore(FilePath);

        Assert.Equal(["alpha", "beta"], reloaded.Repositories.Select(r => r.Name));
    }

    [Fact]
    public void Add_IgnoresDuplicates_WithDifferentCaseOrTrailingSlash()
    {
        var store = new RepositoryStore(FilePath);

        Assert.True(store.Add(RepositoryInfo.FromPath(Code("my-git"))));
        Assert.False(store.Add(RepositoryInfo.FromPath(Code("my-git").ToUpperInvariant() + Path.DirectorySeparatorChar)));
        Assert.Single(store.Repositories);
    }

    [Fact]
    public void Remove_DeletesRepository()
    {
        var store = new RepositoryStore(FilePath);
        store.Add(RepositoryInfo.FromPath(Code("my-git")));

        Assert.True(store.Remove(Code("my-git")));
        Assert.Empty(new RepositoryStore(FilePath).Repositories);
    }

    [Fact]
    public void Load_StartsEmptyAndKeepsBackup_WhenFileIsCorrupted()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, """[{ "Path": "C:\Users\bad-escape" }]""");

        var store = new RepositoryStore(FilePath);

        Assert.Empty(store.Repositories);
        Assert.True(File.Exists(store.BackupFilePath));
    }

    [Fact]
    public void FromPath_UsesFolderNameAsDisplayName()
    {
        Assert.Equal("my-git", RepositoryInfo.FromPath(Code("my-git") + Path.DirectorySeparatorChar).Name);
    }

    public void Dispose() => TempRepository.DeleteFolder(_folder);
}
