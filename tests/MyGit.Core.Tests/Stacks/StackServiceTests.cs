using MyGit.Core.Git;
using MyGit.Core.Operations;
using MyGit.Core.Stacks;

namespace MyGit.Core.Tests.Stacks;

public sealed class StackServiceTests
{
    private readonly GitRunner _git = new();

    private StackService Stacks => new(_git, new RebaseService(_git));

    /// <summary>main → model (1 commit) → ui (1 commit) → save (1 commit), with save checked out.</summary>
    private async Task<TempRepository> ThreeBranchStackAsync()
    {
        var repository = await TempRepository.CreateAsync();
        await repository.RunAsync("config", "core.autocrlf", "false");
        await repository.CommitFileAsync("base.txt", "base\n", "base");

        foreach (var name in new[] { "model", "ui", "save" })
        {
            await Stacks.CreateOnTopAsync(repository.Path, name, "main");
            await repository.CommitFileAsync($"{name}.txt", $"{name}\n", $"{name} work");
        }

        return repository;
    }

    private static async Task<string[]> LogAsync(TempRepository repository, string branch) =>
        [.. (await repository.RunAsync("log", "--format=%s", branch)).OutputLines];

    [Fact]
    public async Task CreateOnTop_BuildsAStack_BottomFirst()
    {
        using var repository = await ThreeBranchStackAsync();

        var stack = Assert.Single(await Stacks.GetStacksAsync(repository.Path));

        Assert.Equal("main", stack.Trunk);
        Assert.Equal(["model", "ui", "save"], stack.Branches.Select(branch => branch.Name));
        Assert.Equal(["main", "model", "ui"], stack.Branches.Select(branch => branch.Parent));
        Assert.All(stack.Branches, branch => Assert.Equal(1, branch.CommitCount));
        Assert.False(stack.NeedsRestack);
        Assert.Equal("save", (await repository.RunAsync("branch", "--show-current")).StandardOutput.Trim());
    }

    [Fact]
    public async Task CreateOnTop_RefusesASecondBranchOnTheSameParent()
    {
        using var repository = await ThreeBranchStackAsync();
        await repository.RunAsync("switch", "-q", "ui");

        await Assert.ThrowsAsync<InvalidOperationException>(() => Stacks.CreateOnTopAsync(repository.Path, "other", "main"));
    }

    [Fact]
    public async Task CreateOnTop_FromAFeatureBranch_MakesItTheBottom()
    {
        using var repository = await TempRepository.CreateAsync();
        await repository.CommitFileAsync("base.txt", "base", "base");
        await repository.RunAsync("switch", "-q", "-c", "feature");
        await repository.CommitFileAsync("f.txt", "f", "feature work");

        await Stacks.CreateOnTopAsync(repository.Path, "feature-2", "main");

        var stack = Assert.Single(await Stacks.GetStacksAsync(repository.Path));
        Assert.Equal(["feature", "feature-2"], stack.Branches.Select(branch => branch.Name));
        Assert.Equal(1, stack.Branches[0].CommitCount);
    }

    [Fact]
    public async Task AmendingALowerBranch_MarksTheOnesAbove_AndRestackMovesThemAll()
    {
        using var repository = await ThreeBranchStackAsync();
        await repository.RunAsync("switch", "-q", "model");
        await File.WriteAllTextAsync(Path.Combine(repository.Path, "model.txt"), "model, amended\n");
        await repository.RunAsync("commit", "-q", "-a", "--amend", "-m", "model work (amended)");

        var stack = Assert.Single(await Stacks.GetStacksAsync(repository.Path));
        Assert.Equal([false, true, false], stack.Branches.Select(branch => branch.NeedsRestack));

        Assert.Equal(RebaseOutcome.Done, await Stacks.RestackAsync(repository.Path, stack, returnTo: "model", autoStash: true));

        Assert.False(Assert.Single(await Stacks.GetStacksAsync(repository.Path)).NeedsRestack);
        Assert.Equal(["save work", "ui work", "model work (amended)", "base"], await LogAsync(repository, "save"));
        Assert.Equal(["ui work", "model work (amended)", "base"], await LogAsync(repository, "ui"));
        Assert.Equal(["model work (amended)", "base"], await LogAsync(repository, "model"));
        Assert.Equal("model, amended\n", (await repository.RunAsync("show", "save:model.txt")).StandardOutput);
        Assert.Equal("model", (await repository.RunAsync("branch", "--show-current")).StandardOutput.Trim());
    }

    [Fact]
    public async Task Restack_WithAMiddleBranchCheckedOut_MovesEveryBranch_AndLeavesTheBottomAlone()
    {
        using var repository = await ThreeBranchStackAsync();
        await repository.RunAsync("switch", "-q", "model");
        await repository.RunAsync("commit", "-q", "--amend", "-m", "model work (amended)");
        var amended = (await repository.RunAsync("rev-parse", "model")).StandardOutput.Trim();
        await repository.RunAsync("switch", "-q", "ui");

        var stack = Assert.Single(await Stacks.GetStacksAsync(repository.Path));
        Assert.Equal(RebaseOutcome.Done, await Stacks.RestackAsync(repository.Path, stack, returnTo: "ui", autoStash: true));

        Assert.Equal(amended, (await repository.RunAsync("rev-parse", "model")).StandardOutput.Trim());
        Assert.Equal(["ui work", "model work (amended)", "base"], await LogAsync(repository, "ui"));
        Assert.Equal(["save work", "ui work", "model work (amended)", "base"], await LogAsync(repository, "save"));
        Assert.Equal("ui", (await repository.RunAsync("branch", "--show-current")).StandardOutput.Trim());
        Assert.False(Assert.Single(await Stacks.GetStacksAsync(repository.Path)).NeedsRestack);
    }

    [Fact]
    public async Task NewCommitsOnTheTrunk_RestackTheWholeStack()
    {
        using var repository = await ThreeBranchStackAsync();
        await repository.RunAsync("switch", "-q", "main");
        await repository.CommitFileAsync("main.txt", "main\n", "main moved on");
        await repository.RunAsync("switch", "-q", "ui");

        var stack = Assert.Single(await Stacks.GetStacksAsync(repository.Path));
        Assert.True(stack.Branches[0].NeedsRestack);

        Assert.Equal(RebaseOutcome.Done, await Stacks.RestackAsync(repository.Path, stack, returnTo: "ui", autoStash: false));

        Assert.Equal(["save work", "ui work", "model work", "main moved on", "base"], await LogAsync(repository, "save"));
        Assert.Equal("ui", (await repository.RunAsync("branch", "--show-current")).StandardOutput.Trim());
    }

    [Fact]
    public async Task Restack_ThatConflicts_Stops_AndRestackingAgainAfterContinueFinishesTheRest()
    {
        using var repository = await ThreeBranchStackAsync();

        // "ui" changes model.txt too; amending model's version of it makes ui conflict when replayed.
        await repository.RunAsync("switch", "-q", "ui");
        await File.WriteAllTextAsync(Path.Combine(repository.Path, "model.txt"), "model, as ui sees it\n");
        await repository.RunAsync("commit", "-q", "-a", "-m", "ui edits model");
        await repository.RunAsync("switch", "-q", "model");
        await File.WriteAllTextAsync(Path.Combine(repository.Path, "model.txt"), "model, amended\n");
        await repository.RunAsync("commit", "-q", "-a", "--amend", "-m", "model work (amended)");

        var stack = (await Stacks.GetStacksAsync(repository.Path)).Single();
        Assert.Equal(RebaseOutcome.Conflict, await Stacks.RestackAsync(repository.Path, stack, returnTo: "model", autoStash: false));

        await File.WriteAllTextAsync(Path.Combine(repository.Path, "model.txt"), "model, both\n");
        await repository.RunAsync("add", "model.txt");
        Assert.Equal(ContinueOutcome.Done, await new OperationService(_git).ContinueAsync(repository.Path, OperationKind.Rebase));

        // ui is done; save (which never had "ui edits model") is the rest, picked up by restacking again.
        stack = (await Stacks.GetStacksAsync(repository.Path)).Single();
        Assert.Equal([false, false, true], stack.Branches.Select(branch => branch.NeedsRestack));
        Assert.Equal(RebaseOutcome.Done, await Stacks.RestackAsync(repository.Path, stack, returnTo: "model", autoStash: false));

        Assert.False((await Stacks.GetStacksAsync(repository.Path)).Single().NeedsRestack);
        Assert.Equal("model, both\n", (await repository.RunAsync("show", "save:model.txt")).StandardOutput);
        Assert.Equal(["save work", "ui edits model", "ui work", "model work (amended)", "base"], await LogAsync(repository, "save"));
    }

    [Fact]
    public async Task Remove_TakesABranchOut_AndTheOneAboveMovesDown()
    {
        using var repository = await ThreeBranchStackAsync();

        await Stacks.RemoveAsync(repository.Path, "ui");

        var stack = Assert.Single(await Stacks.GetStacksAsync(repository.Path));
        Assert.Equal(["model", "save"], stack.Branches.Select(branch => branch.Name));
        Assert.Equal("model", stack.Branches[1].Parent);
    }

    [Fact]
    public async Task Push_PublishesEveryBranch_AndForcePushesAfterARestack()
    {
        using var repository = await ThreeBranchStackAsync();
        var remote = TempRepository.CreateEmptyFolder();
        try
        {
            await _git.RunCheckedAsync(remote, ["init", "--bare", "-q", "-b", "main"]);
            await repository.RunAsync("remote", "add", "origin", remote);
            await repository.RunAsync("push", "-q", "origin", "main");

            var stack = (await Stacks.GetStacksAsync(repository.Path)).Single();
            Assert.True((await Stacks.PushAsync(repository.Path, stack)).Succeeded);
            Assert.Equal("origin/ui", (await repository.RunAsync("rev-parse", "--abbrev-ref", "ui@{u}")).StandardOutput.Trim());

            // Amend the bottom, restack, push again: the rewritten branches go up.
            await repository.RunAsync("switch", "-q", "model");
            await repository.RunAsync("commit", "-q", "--amend", "-m", "model work (amended)");
            stack = (await Stacks.GetStacksAsync(repository.Path)).Single();
            await Stacks.RestackAsync(repository.Path, stack, "model", autoStash: false);
            stack = (await Stacks.GetStacksAsync(repository.Path)).Single();

            Assert.True((await Stacks.PushAsync(repository.Path, stack)).Succeeded);
            var remoteLog = (await _git.RunCheckedAsync(remote, ["log", "--format=%s", "save"])).OutputLines;
            Assert.Equal(["save work", "ui work", "model work (amended)", "base"], remoteLog);
        }
        finally
        {
            TempRepository.DeleteFolder(remote);
        }
    }

    [Fact]
    public void StackDescription_AddsTheSection_ThenReplacesIt()
    {
        var section = StackDescription.Section("main", [14, 15, 16], current: 1);
        Assert.Contains("Stack (from main up): #14 → **#15** → #16", section);

        var body = StackDescription.Apply("## What\n\nThe UI.", section);
        Assert.StartsWith("## What\n\nThe UI.\n\n" + StackDescription.Start, body);

        var updated = StackDescription.Apply(body, StackDescription.Section("main", [15, 16], current: 0));
        Assert.Contains("**#15** → #16", updated);
        Assert.DoesNotContain("#14", updated);
        Assert.StartsWith("## What\n\nThe UI.", updated);
    }
}
