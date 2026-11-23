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
    public async Task GetLastMessageAsync_ReturnsNull_WithoutCommits()
    {
        using var repository = await TempRepository.CreateAsync();

        Assert.Null(await _service.GetLastMessageAsync(repository.Path));
    }
}
