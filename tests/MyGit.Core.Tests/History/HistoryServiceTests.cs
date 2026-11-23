using MyGit.Core.Changes;
using MyGit.Core.Git;
using MyGit.Core.History;

namespace MyGit.Core.Tests.History;

public sealed class HistoryServiceTests
{
    private readonly HistoryService _history = new(new GitRunner());

    [Fact]
    public void ParseRefs_ReadsBranchesTagsAndHead()
    {
        var refs = HistoryService.ParseRefs("HEAD -> feature/x, origin/main, origin/HEAD, tag: v1.2.0, main");

        Assert.Equal(
            [
                new RefLabel(RefKind.Head, "HEAD"),
                new RefLabel(RefKind.LocalBranch, "feature/x"),
                new RefLabel(RefKind.RemoteBranch, "origin/main"),
                new RefLabel(RefKind.Tag, "v1.2.0"),
                new RefLabel(RefKind.LocalBranch, "main"),
            ],
            refs);
    }

    [Fact]
    public async Task GetCommitsAsync_ListsNewestFirst_WithTagsAndBranchLabels()
    {
        using var repository = await TempRepository.CreateAsync();
        await repository.CommitFileAsync("a.txt", "1", "First");
        await repository.RunAsync("tag", "v1.0.0");
        await repository.CommitFileAsync("a.txt", "2", "Second");
        await repository.RunAsync("branch", "feature/slots");

        var commits = await _history.ClassifyRefsAsync(repository.Path, await _history.GetCommitsAsync(repository.Path, 0, 50));

        Assert.Equal(["Second", "First"], commits.Select(c => c.Subject));
        Assert.Contains(new RefLabel(RefKind.LocalBranch, "feature/slots"), commits[0].Refs); // slash name fixed up to local
        Assert.Contains(new RefLabel(RefKind.Head, "HEAD"), commits[0].Refs);
        Assert.Contains(new RefLabel(RefKind.Tag, "v1.0.0"), commits[1].Refs);
        Assert.Single(commits[0].Parents);
        Assert.Empty(commits[1].Parents);
    }

    [Fact]
    public async Task GetCommitsAsync_PagesWithSkip_AndIsEmptyWithoutCommits()
    {
        using var empty = await TempRepository.CreateAsync();
        Assert.Empty(await _history.GetCommitsAsync(empty.Path, 0, 10));

        using var repository = await TempRepository.CreateAsync();
        for (var i = 1; i <= 5; i++)
        {
            await repository.CommitFileAsync("a.txt", i.ToString(), $"Commit {i}");
        }

        var page = await _history.GetCommitsAsync(repository.Path, 2, 2);
        Assert.Equal(["Commit 3", "Commit 2"], page.Select(c => c.Subject));
    }

    [Fact]
    public async Task GetDetailsAsync_ListsFiles_IncludingTheFirstCommit()
    {
        using var repository = await TempRepository.CreateAsync();
        await repository.CommitFileAsync("a.txt", "one\n", "First\n\nLonger description.");
        await File.WriteAllTextAsync(Path.Combine(repository.Path, "b.txt"), "new");
        await File.WriteAllTextAsync(Path.Combine(repository.Path, "a.txt"), "two\n");
        await repository.RunAsync("add", ".");
        await repository.RunAsync("commit", "-m", "Second");
        var commits = await _history.GetCommitsAsync(repository.Path, 0, 10);

        var second = await _history.GetDetailsAsync(repository.Path, commits[0].Sha);
        Assert.Equal([("a.txt", ChangeKind.Modified), ("b.txt", ChangeKind.Added)], second.Files.Select(f => (f.Path, f.Kind)));

        var first = await _history.GetDetailsAsync(repository.Path, commits[1].Sha);
        Assert.Equal("First\n\nLonger description.", first.Message);
        Assert.Equal([("a.txt", ChangeKind.Added)], first.Files.Select(f => (f.Path, f.Kind)));

        var diff = await _history.GetFileDiffAsync(repository.Path, second, second.Files[0]);
        Assert.Contains(diff.Hunks[0].Lines, line => line.Text == "two");
    }

    [Fact]
    public async Task CreateBranchAndTag_FromAnOldCommit()
    {
        using var repository = await TempRepository.CreateAsync();
        await repository.CommitFileAsync("a.txt", "1", "First");
        await repository.CommitFileAsync("a.txt", "2", "Second");
        var first = (await _history.GetCommitsAsync(repository.Path, 0, 10))[1];

        await _history.CreateTagAsync(repository.Path, "v0.1", first.Sha, "Early version", push: false);
        await _history.CreateBranchAsync(repository.Path, "hotfix", "v0.1", switchToIt: true);

        var branch = await repository.RunAsync("branch", "--show-current");
        Assert.Equal("hotfix", branch.StandardOutput.Trim());
        var head = await repository.RunAsync("rev-parse", "HEAD");
        Assert.Equal(first.Sha, head.StandardOutput.Trim());
        var tagType = await repository.RunAsync("cat-file", "-t", "v0.1");
        Assert.Equal("tag", tagType.StandardOutput.Trim()); // annotated, because it has a message
    }

    [Fact]
    public async Task ValidateNames_RejectsBadAndExistingNames()
    {
        using var repository = await TempRepository.CreateAsync();
        await repository.CommitFileAsync("a.txt", "1", "First");
        await repository.RunAsync("tag", "v1");

        Assert.NotNull(await _history.ValidateBranchNameAsync(repository.Path, "has space"));
        Assert.NotNull(await _history.ValidateBranchNameAsync(repository.Path, "main"));
        Assert.Null(await _history.ValidateBranchNameAsync(repository.Path, "feature/new-thing"));
        Assert.NotNull(await _history.ValidateTagNameAsync(repository.Path, "v1"));
        Assert.Null(await _history.ValidateTagNameAsync(repository.Path, "v1.1"));
    }
}
