using MyGit.Core.Git;
using MyGit.Core.Repositories;

namespace MyGit.Core.Tests.Repositories;

public sealed class RepositoryServiceTests
{
    private readonly RepositoryService _service = new(new GitRunner());

    [Fact]
    public async Task FindRootAsync_ReturnsRoot_FromSubfolder()
    {
        using var repository = await TempRepository.CreateAsync();
        var subfolder = Directory.CreateDirectory(Path.Combine(repository.Path, "src", "deep")).FullName;

        var root = await _service.FindRootAsync(subfolder);

        Assert.NotNull(root);
        Assert.Equal(Path.GetFullPath(repository.Path), root, ignoreCase: true);
    }

    [Fact]
    public async Task FindRootAsync_ReturnsNull_ForPlainFolder()
    {
        var folder = TempRepository.CreateEmptyFolder();
        try
        {
            Assert.Null(await _service.FindRootAsync(folder));
        }
        finally
        {
            TempRepository.DeleteFolder(folder);
        }
    }

    [Fact]
    public async Task FindRootAsync_ReturnsNull_ForMissingFolder()
    {
        var missing = Path.Combine(Path.GetTempPath(), "mygit-tests", "does-not-exist-" + Guid.NewGuid());

        Assert.Null(await _service.FindRootAsync(missing));
    }

    [Fact]
    public async Task GetCurrentBranchAsync_ReturnsBranchName()
    {
        using var repository = await TempRepository.CreateAsync("main");
        await repository.CommitFileAsync("a.txt", "a", "first");
        await repository.RunAsync("checkout", "-b", "feature/login");

        Assert.Equal("feature/login", await _service.GetCurrentBranchAsync(repository.Path));
    }

    [Fact]
    public async Task GetCurrentBranchAsync_ReturnsNull_WhenDetached()
    {
        using var repository = await TempRepository.CreateAsync();
        await repository.CommitFileAsync("a.txt", "a", "first");
        await repository.RunAsync("checkout", "--detach");

        Assert.Null(await _service.GetCurrentBranchAsync(repository.Path));
    }
}
