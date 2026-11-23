using MyGit.Core.Commits;
using MyGit.Core.Git;

namespace MyGit.Core.Tests.Commits;

public sealed class CommitServiceTests
{
    private readonly CommitService _service = new(new GitRunner());

    [Fact]
    public void BuildAndSplitMessage_RoundTrip()
    {
        var message = CommitService.BuildMessage("  Add inventory slots ", "Slots hold items.\nMax 24.");

        Assert.Equal("Add inventory slots\n\nSlots hold items.\nMax 24.", message);
        Assert.Equal(("Add inventory slots", "Slots hold items.\nMax 24."), CommitService.SplitMessage(message));
        Assert.Equal("Only summary", CommitService.BuildMessage("Only summary", "   "));
    }

    [Fact]
    public async Task CommitAsync_CommitsStagedChangesWithMessage()
    {
        using var repository = await TempRepository.CreateAsync();
        await File.WriteAllTextAsync(Path.Combine(repository.Path, "a.txt"), "one");
        await repository.RunAsync("add", "a.txt");

        await _service.CommitAsync(repository.Path, "First commit\n\nWith \"quotes\" and ñ", amend: false);

        Assert.Equal("First commit\n\nWith \"quotes\" and ñ", await _service.GetLastMessageAsync(repository.Path));
    }

    [Fact]
    public async Task CommitAsync_AmendReplacesLastCommit()
    {
        using var repository = await TempRepository.CreateAsync();
        await repository.CommitFileAsync("a.txt", "one", "Typo mesage");
        await File.WriteAllTextAsync(Path.Combine(repository.Path, "b.txt"), "forgotten file");
        await repository.RunAsync("add", "b.txt");

        await _service.CommitAsync(repository.Path, "Fixed message", amend: true);

        var log = await repository.RunAsync("log", "--format=%s");
        Assert.Equal("Fixed message", log.StandardOutput.Trim());
        var files = await repository.RunAsync("show", "--name-only", "--format=", "HEAD");
        Assert.Contains("b.txt", files.StandardOutput);
    }

    [Fact]
    public async Task CommitAsync_ReturnsInfo_AndUndoRestagesEverything()
    {
        using var repository = await TempRepository.CreateAsync();
        await repository.CommitFileAsync("a.txt", "one", "first");
        await File.WriteAllTextAsync(Path.Combine(repository.Path, "a.txt"), "two");
        await File.WriteAllTextAsync(Path.Combine(repository.Path, "b.txt"), "new");
        await repository.RunAsync("add", "a.txt", "b.txt");

        var info = await _service.CommitAsync(repository.Path, "Second\n\nDetails", amend: false);

        Assert.Equal("Second", info.Summary);
        Assert.Equal(2, info.FileCount);
        Assert.StartsWith(info.ShortSha, info.Sha);

        Assert.Null(await _service.UndoAsync(repository.Path, info));

        var log = await repository.RunAsync("log", "--format=%s");
        Assert.Equal("first", log.StandardOutput.Trim());
        var staged = await repository.RunAsync("diff", "--cached", "--name-only");
        Assert.Equal(["a.txt", "b.txt"], staged.OutputLines);
        Assert.Equal("two", await File.ReadAllTextAsync(Path.Combine(repository.Path, "a.txt")));
    }

    [Fact]
    public async Task UndoAsync_OfAmend_BringsBackTheOriginalCommit()
    {
        using var repository = await TempRepository.CreateAsync();
        await repository.CommitFileAsync("a.txt", "one", "Original");
        var original = await _service.GetHeadAsync(repository.Path);

        var amended = await _service.CommitAsync(repository.Path, "Reworded", amend: true);
        Assert.Null(await _service.UndoAsync(repository.Path, amended));

        Assert.Equal(original, await _service.GetHeadAsync(repository.Path));
    }

    [Fact]
    public async Task UndoAsync_OfFirstCommit_LeavesFilesStaged()
    {
        using var repository = await TempRepository.CreateAsync();
        await File.WriteAllTextAsync(Path.Combine(repository.Path, "a.txt"), "one");
        await repository.RunAsync("add", "a.txt");
        var info = await _service.CommitAsync(repository.Path, "First", amend: false);

        Assert.Null(await _service.UndoAsync(repository.Path, info));

        Assert.Null(await _service.GetHeadAsync(repository.Path));
        var staged = await repository.RunAsync("diff", "--cached", "--name-only");
        Assert.Equal(["a.txt"], staged.OutputLines);
    }

    [Fact]
    public async Task UndoAsync_Refuses_WhenTheBranchMovedOn()
    {
        using var repository = await TempRepository.CreateAsync();
        await repository.CommitFileAsync("a.txt", "one", "first");
        await File.WriteAllTextAsync(Path.Combine(repository.Path, "b.txt"), "b");
        await repository.RunAsync("add", "b.txt");
        var info = await _service.CommitAsync(repository.Path, "second", amend: false);
        await repository.CommitFileAsync("c.txt", "c", "third, made elsewhere");

        Assert.NotNull(await _service.UndoAsync(repository.Path, info));
    }

    [Fact]
    public async Task GetLastMessageAsync_ReturnsNull_WithoutCommits()
    {
        using var repository = await TempRepository.CreateAsync();

        Assert.Null(await _service.GetLastMessageAsync(repository.Path));
    }
}
