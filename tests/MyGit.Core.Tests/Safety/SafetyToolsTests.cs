using MyGit.Core.Git;
using MyGit.Core.History;
using MyGit.Core.Stashes;
using MyGit.Core.Undo;

namespace MyGit.Core.Tests.Safety;

public sealed class SafetyToolsTests
{
    private readonly GitRunner _git = new();

    // ---- Stashes ----

    [Theory]
    [InlineData("On main: WIP inventory UI", "main", "WIP inventory UI")]
    [InlineData("WIP on feature/x: a1b2c3d Add slots", "feature/x", "Work in progress on feature/x")]
    public void ParseSubject_ReadsBranchAndMessage(string subject, string branch, string message)
    {
        Assert.Equal((branch, message), StashService.ParseSubject(subject));
    }

    [Fact]
    public async Task Stash_PushListFilesDiffAndPop()
    {
        using var repository = await TempRepository.CreateAsync();
        await repository.CommitFileAsync("a.txt", "one\n", "first");
        await File.WriteAllTextAsync(Path.Combine(repository.Path, "a.txt"), "two\n");
        await File.WriteAllTextAsync(Path.Combine(repository.Path, "new.txt"), "brand new\n");
        var stashes = new StashService(_git);

        Assert.True(await stashes.PushAsync(repository.Path, "WIP inventory UI"));
        // git may restore the file with Windows line endings; only the content matters here.
        Assert.Equal("one\n", (await File.ReadAllTextAsync(Path.Combine(repository.Path, "a.txt"))).ReplaceLineEndings("\n"));
        Assert.False(File.Exists(Path.Combine(repository.Path, "new.txt")));

        var stash = Assert.Single(await stashes.ListAsync(repository.Path));
        Assert.Equal(("WIP inventory UI", "main"), (stash.Message, stash.Branch));

        var files = await stashes.GetFilesAsync(repository.Path, stash);
        Assert.Equal(["a.txt", "new.txt"], files.Select(f => f.Path));

        var newFileDiff = await stashes.GetFileDiffAsync(repository.Path, stash, files[1]);
        Assert.Contains(newFileDiff.Hunks[0].Lines, line => line.Text == "brand new");

        Assert.Equal(StashApplyOutcome.Done, await stashes.ApplyAsync(repository.Path, stash, remove: true));
        Assert.Equal("two\n", (await File.ReadAllTextAsync(Path.Combine(repository.Path, "a.txt"))).ReplaceLineEndings("\n"));
        Assert.Empty(await stashes.ListAsync(repository.Path));
    }

    [Fact]
    public async Task Stash_OnlySelectedFiles_AndNothingToStash()
    {
        using var repository = await TempRepository.CreateAsync();
        await repository.CommitFileAsync("a.txt", "one", "first");
        await repository.CommitFileAsync("b.txt", "one", "second");
        var stashes = new StashService(_git);

        Assert.False(await stashes.PushAsync(repository.Path, null));

        await File.WriteAllTextAsync(Path.Combine(repository.Path, "a.txt"), "changed");
        await File.WriteAllTextAsync(Path.Combine(repository.Path, "b.txt"), "changed");
        Assert.True(await stashes.PushAsync(repository.Path, "only a", ["a.txt"]));

        Assert.Equal("one", await File.ReadAllTextAsync(Path.Combine(repository.Path, "a.txt")));
        Assert.Equal("changed", await File.ReadAllTextAsync(Path.Combine(repository.Path, "b.txt")));
    }

    // ---- Undo ----

    [Theory]
    [InlineData("commit: Add slot limit", "commit “Add slot limit”")]
    [InlineData("commit (amend): Fixed", "amend “Fixed”")]
    [InlineData("rebase (finish): refs/heads/x onto 1a2b3c", "rebase")]
    [InlineData("pull --rebase (finish): refs/heads/x onto 1a2b3c", "pull with rebase")]
    [InlineData("reset: moving to origin/main", "reset to origin/main")]
    public void Describe_UsesPlainWords(string subject, string expected)
    {
        Assert.Equal(expected, UndoService.Describe(subject));
    }

    [Fact]
    public async Task Undo_LastCommit_KeepsUncommittedChanges_AndCanBeUndoneAgain()
    {
        using var repository = await TempRepository.CreateAsync();
        await repository.CommitFileAsync("a.txt", "one", "first");
        await repository.CommitFileAsync("b.txt", "two", "Add b");
        await File.WriteAllTextAsync(Path.Combine(repository.Path, "notes.txt"), "uncommitted");
        var undo = new UndoService(_git);

        var points = await undo.GetPointsAsync(repository.Path);
        Assert.Equal("commit “Add b”", points[0].Description);

        Assert.Equal(UndoOutcome.Done, await undo.UndoAsync(repository.Path, points[0]));
        Assert.Equal("first", (await repository.RunAsync("log", "-1", "--format=%s")).StandardOutput.Trim());
        Assert.True(File.Exists(Path.Combine(repository.Path, "notes.txt")));

        // The undo itself is now the newest point: undoing it brings the commit back.
        var again = await undo.GetPointsAsync(repository.Path);
        Assert.StartsWith("reset", again[0].Description);
        Assert.Equal(UndoOutcome.Done, await undo.UndoAsync(repository.Path, again[0]));
        Assert.Equal("Add b", (await repository.RunAsync("log", "-1", "--format=%s")).StandardOutput.Trim());
    }

    [Fact]
    public async Task Undo_Refuses_WhenUncommittedChangesWouldBeLost()
    {
        using var repository = await TempRepository.CreateAsync();
        await repository.CommitFileAsync("a.txt", "one", "first");
        await repository.CommitFileAsync("a.txt", "two", "second");
        await File.WriteAllTextAsync(Path.Combine(repository.Path, "a.txt"), "editing a.txt");
        var undo = new UndoService(_git);

        var points = await undo.GetPointsAsync(repository.Path);

        Assert.Equal(UndoOutcome.WouldLoseChanges, await undo.UndoAsync(repository.Path, points[0]));
        Assert.Equal("editing a.txt", await File.ReadAllTextAsync(Path.Combine(repository.Path, "a.txt")));
    }

    // ---- Cherry-pick, revert, reset ----

    [Fact]
    public async Task CherryPick_CopiesACommitFromAnotherBranch()
    {
        using var repository = await TempRepository.CreateAsync();
        await repository.CommitFileAsync("a.txt", "one", "first");
        await repository.RunAsync("checkout", "-q", "-b", "other");
        await repository.CommitFileAsync("fix.txt", "fix", "The fix");
        await repository.RunAsync("checkout", "-q", "main");
        var history = new HistoryService(_git);
        var fix = (await history.GetCommitsAsync(repository.Path, 0, 10)).Count == 1
            ? (await HistoryOfAsync(repository, "other"))[0]
            : throw new InvalidOperationException();

        Assert.Equal(CommitActionOutcome.Done, await new CommitActionsService(_git).CherryPickAsync(repository.Path, fix));
        Assert.True(File.Exists(Path.Combine(repository.Path, "fix.txt")));

        // Picking it again: nothing to do, and no half-done cherry-pick left behind.
        Assert.Equal(CommitActionOutcome.NothingToDo, await new CommitActionsService(_git).CherryPickAsync(repository.Path, fix));
        Assert.Empty((await repository.RunAsync("status", "--porcelain")).OutputLines);
    }

    [Fact]
    public async Task Revert_AddsACommitThatUndoesIt()
    {
        using var repository = await TempRepository.CreateAsync();
        await repository.CommitFileAsync("a.txt", "one", "first");
        await repository.CommitFileAsync("a.txt", "two", "Change a");
        var change = (await new HistoryService(_git).GetCommitsAsync(repository.Path, 0, 10))[0];

        Assert.Equal(CommitActionOutcome.Done, await new CommitActionsService(_git).RevertAsync(repository.Path, change));

        Assert.Equal("one", await File.ReadAllTextAsync(Path.Combine(repository.Path, "a.txt")));
        Assert.StartsWith("Revert \"Change a\"", (await repository.RunAsync("log", "-1", "--format=%s")).StandardOutput.Trim());
    }

    [Theory]
    [InlineData(ResetMode.Soft, true, false)]
    [InlineData(ResetMode.Mixed, false, true)]
    [InlineData(ResetMode.Hard, false, false)]
    public async Task Reset_Modes(ResetMode mode, bool expectStaged, bool expectUnstaged)
    {
        using var repository = await TempRepository.CreateAsync();
        await repository.CommitFileAsync("a.txt", "one", "first");
        await repository.CommitFileAsync("a.txt", "two", "second");
        var commits = await new HistoryService(_git).GetCommitsAsync(repository.Path, 0, 10);
        var actions = new CommitActionsService(_git);

        Assert.Equal(1, await actions.CountCommitsAfterAsync(repository.Path, commits[1].Sha));
        await actions.ResetAsync(repository.Path, commits[1].Sha, mode);

        var staged = (await repository.RunAsync("diff", "--cached", "--name-only")).OutputLines.Count > 0;
        var unstaged = (await repository.RunAsync("diff", "--name-only")).OutputLines.Count > 0;
        Assert.Equal((expectStaged, expectUnstaged), (staged, unstaged));
        Assert.Equal("first", (await repository.RunAsync("log", "-1", "--format=%s")).StandardOutput.Trim());
    }

    private async Task<IReadOnlyList<CommitSummary>> HistoryOfAsync(TempRepository repository, string branch)
    {
        var log = await repository.RunAsync("log", "--format=%H%x00%P%x00%an%x00%at%x00%s%x00%D%x1e", branch);
        return HistoryService.ParseLog(log.StandardOutput);
    }
}
