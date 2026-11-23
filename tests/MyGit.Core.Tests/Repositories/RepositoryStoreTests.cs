using MyGit.Core.Repositories;

namespace MyGit.Core.Tests.Repositories;

public sealed class RepositoryStoreTests : IDisposable
{
    private readonly string _folder = TempRepository.CreateEmptyFolder();

    private string FilePath => Path.Combine(_folder, "settings", "repositories.json");

    [Fact]
    public void Add_PersistsAcrossInstances()
    {
        var store = new RepositoryStore(FilePath);
        store.Add(RepositoryInfo.FromPath(@"C:\code\beta"));
        store.Add(RepositoryInfo.FromPath(@"C:\code\alpha"));

        var reloaded = new RepositoryStore(FilePath);

        Assert.Equal(["alpha", "beta"], reloaded.Repositories.Select(r => r.Name));
    }

    [Fact]
    public void Add_IgnoresDuplicates_WithDifferentCaseOrTrailingSlash()
    {
        var store = new RepositoryStore(FilePath);

        Assert.True(store.Add(RepositoryInfo.FromPath(@"C:\code\my-git")));
        Assert.False(store.Add(RepositoryInfo.FromPath(@"c:\CODE\my-git\")));
        Assert.Single(store.Repositories);
    }

    [Fact]
    public void Remove_DeletesRepository()
    {
        var store = new RepositoryStore(FilePath);
        store.Add(RepositoryInfo.FromPath(@"C:\code\my-git"));

        Assert.True(store.Remove(@"C:\code\my-git"));
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
        Assert.Equal("my-git", RepositoryInfo.FromPath(@"C:\code\my-git\").Name);
    }

    public void Dispose() => TempRepository.DeleteFolder(_folder);
}
