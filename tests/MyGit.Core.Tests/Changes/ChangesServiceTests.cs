using MyGit.Core.Changes;
using MyGit.Core.Git;

namespace MyGit.Core.Tests.Changes;

public sealed class ChangesServiceTests
{
    private readonly ChangesService _service = new(new GitRunner(), File.Delete);

    [Fact]
    public async Task GetStatusAsync_ListsUnstagedAndStaged()
    {
        using var repository = await TempRepository.CreateAsync();
        await repository.CommitFileAsync("tracked.txt", "one", "first");
        await repository.CommitFileAsync("staged.txt", "one", "second");

        await File.WriteAllTextAsync(Path.Combine(repository.Path, "tracked.txt"), "changed");
        await File.WriteAllTextAsync(Path.Combine(repository.Path, "staged.txt"), "changed");
        await repository.RunAsync("add", "staged.txt");
        Directory.CreateDirectory(Path.Combine(repository.Path, "Assets", "Scripts"));
        await File.WriteAllTextAsync(Path.Combine(repository.Path, "Assets", "Scripts", "new.cs"), "class A {}");

        var status = await _service.GetStatusAsync(repository.Path);

        Assert.Equal(
            [("Assets/Scripts/new.cs", ChangeKind.Untracked), ("tracked.txt", ChangeKind.Modified)],
            status.Unstaged.Select(change => (change.Path, change.Kind)));
        Assert.Equal([("staged.txt", ChangeKind.Modified)], status.Staged.Select(change => (change.Path, change.Kind)));
    }

    [Fact]
    public async Task GetStatusAsync_ShowsRenameWithOriginalPath()
    {
        using var repository = await TempRepository.CreateAsync();
        await repository.CommitFileAsync("old.txt", "same content for rename detection", "first");
        await repository.RunAsync("mv", "old.txt", "new.txt");

        var status = await _service.GetStatusAsync(repository.Path);

        var rename = Assert.Single(status.Staged);
        Assert.Equal(ChangeKind.Renamed, rename.Kind);
        Assert.Equal("new.txt", rename.Path);
        Assert.Equal("old.txt", rename.OriginalPath);
    }

    [Fact]
    public async Task GetStatusAsync_HandlesSpacesAndNonAscii()
    {
        using var repository = await TempRepository.CreateAsync();
        await File.WriteAllTextAsync(Path.Combine(repository.Path, "mi archivo ñ.txt"), "hola");

        var status = await _service.GetStatusAsync(repository.Path);

        Assert.Equal("mi archivo ñ.txt", Assert.Single(status.Unstaged).Path);
    }

    [Fact]
    public async Task StageAndUnstage_MoveFilesBetweenLists()
    {
        using var repository = await TempRepository.CreateAsync();
        await repository.CommitFileAsync("a.txt", "one", "first");
        await File.WriteAllTextAsync(Path.Combine(repository.Path, "a.txt"), "two");
        File.Delete(Path.Combine(repository.Path, "a.txt"));
        await File.WriteAllTextAsync(Path.Combine(repository.Path, "b.txt"), "new");

        await _service.StageAsync(repository.Path, ["a.txt", "b.txt"]);
        var staged = await _service.GetStatusAsync(repository.Path);

        Assert.Empty(staged.Unstaged);
        Assert.Equal([("a.txt", ChangeKind.Deleted), ("b.txt", ChangeKind.Added)], staged.Staged.Select(c => (c.Path, c.Kind)));

        await _service.UnstageAsync(repository.Path, ["a.txt", "b.txt"]);
        var unstaged = await _service.GetStatusAsync(repository.Path);

        Assert.Empty(unstaged.Staged);
        Assert.Equal(2, unstaged.Unstaged.Count);
    }

    [Fact]
    public async Task UnstageAsync_WorksBeforeTheFirstCommit()
    {
        using var repository = await TempRepository.CreateAsync();
        await File.WriteAllTextAsync(Path.Combine(repository.Path, "a.txt"), "new");
        await _service.StageAsync(repository.Path, ["a.txt"]);

        await _service.UnstageAsync(repository.Path, ["a.txt"]);

        var status = await _service.GetStatusAsync(repository.Path);
        Assert.Empty(status.Staged);
        Assert.Equal(ChangeKind.Untracked, Assert.Single(status.Unstaged).Kind);
    }

    [Fact]
    public async Task DiscardAsync_RestoresTrackedAndRemovesUntracked()
    {
        using var repository = await TempRepository.CreateAsync();
        await repository.CommitFileAsync("a.txt", "original", "first");
        await File.WriteAllTextAsync(Path.Combine(repository.Path, "a.txt"), "changed");
        await File.WriteAllTextAsync(Path.Combine(repository.Path, "junk.txt"), "junk");
        var status = await _service.GetStatusAsync(repository.Path);

        await _service.DiscardAsync(repository.Path, status.Unstaged);

        Assert.Equal("original", await File.ReadAllTextAsync(Path.Combine(repository.Path, "a.txt")));
        Assert.False(File.Exists(Path.Combine(repository.Path, "junk.txt")));
        Assert.Empty((await _service.GetStatusAsync(repository.Path)).Unstaged);
    }

    [Fact]
    public async Task DiscardAsync_KeepsStagedChanges()
    {
        using var repository = await TempRepository.CreateAsync();
        await repository.CommitFileAsync("a.txt", "original", "first");
        await File.WriteAllTextAsync(Path.Combine(repository.Path, "a.txt"), "staged");
        await repository.RunAsync("add", "a.txt");
        await File.WriteAllTextAsync(Path.Combine(repository.Path, "a.txt"), "staged plus more");
        var status = await _service.GetStatusAsync(repository.Path);

        await _service.DiscardAsync(repository.Path, status.Unstaged);

        Assert.Equal("staged", await File.ReadAllTextAsync(Path.Combine(repository.Path, "a.txt")));
        Assert.Single((await _service.GetStatusAsync(repository.Path)).Staged);
    }

    [Fact]
    public async Task StageAsync_HandlesManyFiles()
    {
        using var repository = await TempRepository.CreateAsync();
        var paths = Enumerable.Range(0, 250).Select(i => $"file{i:000}.txt").ToList();
        foreach (var path in paths)
        {
            await File.WriteAllTextAsync(Path.Combine(repository.Path, path), path);
        }

        await _service.StageAsync(repository.Path, paths);

        Assert.Equal(250, (await _service.GetStatusAsync(repository.Path)).Staged.Count);
    }
}
