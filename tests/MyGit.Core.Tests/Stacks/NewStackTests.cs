using MyGit.Core.Git;
using MyGit.Core.Operations;
using MyGit.Core.Stacks;

namespace MyGit.Core.Tests.Stacks;

public sealed class NewStackTests
{
    private readonly GitRunner _git = new();

    private StackService Stacks => new(_git, new RebaseService(_git));

    /// <summary>
    /// Plain branches (made with "git switch -c", not as a stack): main → model (1) → ui (2) → save (1),
    /// plus "old-experiment" on ui's commit and "elsewhere" off main.
    /// </summary>
    private static async Task<TempRepository> PlainBranchesAsync()
    {
        var repository = await TempRepository.CreateAsync();
        await repository.CommitFileAsync("base.txt", "base", "base");
        await repository.RunAsync("switch", "-q", "-c", "model");
        await repository.CommitFileAsync("model.txt", "m", "model work");
        await repository.RunAsync("switch", "-q", "-c", "ui");
        await repository.CommitFileAsync("ui.txt", "u", "ui work");
        await repository.CommitFileAsync("ui2.txt", "u2", "more ui work");
        await repository.RunAsync("branch", "old-experiment");
        await repository.RunAsync("switch", "-q", "-c", "save");
        await repository.CommitFileAsync("save.txt", "s", "save work");
        await repository.RunAsync("branch", "elsewhere", "main");
        await repository.RunAsync("switch", "-q", "elsewhere");
        await repository.CommitFileAsync("other.txt", "o", "unrelated");
        await repository.RunAsync("switch", "-q", "save");
        return repository;
    }

    [Fact]
    public async Task Candidates_AreTheBranchesBelowTheTop_TopFirst_WithDuplicatesNotSuggested()
    {
        using var repository = await PlainBranchesAsync();

        var candidates = await Stacks.FindCandidatesAsync(repository.Path, "save", "main");

        Assert.Equal(["save", "ui", "old-experiment", "model"], candidates.Select(candidate => candidate.Name));
        Assert.Equal([1, 2, 2, 1], candidates.Select(candidate => candidate.CommitCount));
        Assert.Equal([true, true, false, true], candidates.Select(candidate => candidate.IsSuggested));
        Assert.Equal("ui", candidates[2].SameCommitAs);
        Assert.DoesNotContain(candidates, candidate => candidate.Name is "elsewhere" or "main");
    }

    [Fact]
    public async Task CreateStack_LinksTheChosenBranches_BottomFirst()
    {
        using var repository = await PlainBranchesAsync();

        await Stacks.CreateStackAsync(repository.Path, "main", ["model", "ui", "save"]);

        var stack = Assert.Single(await Stacks.GetStacksAsync(repository.Path));
        Assert.Equal(["model", "ui", "save"], stack.Branches.Select(branch => branch.Name));
        Assert.Equal([1, 2, 1], stack.Branches.Select(branch => branch.CommitCount));
        Assert.False(stack.NeedsRestack);

        // Stacked branches aren't offered again for another stack.
        Assert.DoesNotContain(await Stacks.FindCandidatesAsync(repository.Path, "old-experiment", "main"), candidate => candidate.Name == "ui");
    }

    [Fact]
    public async Task CreateStack_WithABranchThatWasNotRebuilt_NeedsARestack()
    {
        using var repository = await PlainBranchesAsync();
        await repository.RunAsync("switch", "-q", "model");
        await repository.RunAsync("commit", "-q", "--amend", "-m", "model work (amended)");
        await repository.RunAsync("switch", "-q", "save");

        await Stacks.CreateStackAsync(repository.Path, "main", ["model", "ui", "save"]);

        var stack = Assert.Single(await Stacks.GetStacksAsync(repository.Path));
        Assert.True(stack.Branches[1].NeedsRestack);
        Assert.Equal(RebaseOutcome.Done, await Stacks.RestackAsync(repository.Path, stack, "save", autoStash: false));
        Assert.Equal(["save work", "more ui work", "ui work", "model work (amended)", "base"],
            (await repository.RunAsync("log", "--format=%s", "save")).OutputLines);
    }
}
