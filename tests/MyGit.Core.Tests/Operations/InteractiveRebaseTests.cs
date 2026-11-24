using MyGit.Core.Git;
using MyGit.Core.Operations;

namespace MyGit.Core.Tests.Operations;

public sealed class InteractiveRebaseTests
{
    private readonly GitRunner _git = new();

    /// <summary>main: "base", then four commits on top: one, two, three, four (each adds its own file).</summary>
    private static async Task<(TempRepository Repository, string Base)> FourCommitsAsync()
    {
        var repository = await TempRepository.CreateAsync();
        await repository.RunAsync("config", "core.autocrlf", "false");
        await repository.CommitFileAsync("base.txt", "base\n", "base");
        var @base = (await repository.RunAsync("rev-parse", "HEAD")).StandardOutput.Trim();

        foreach (var name in new[] { "one", "two", "three", "four" })
        {
            await repository.CommitFileAsync($"{name}.txt", $"{name}\n", name);
        }

        return (repository, @base);
    }

    private async Task<List<RebaseStep>> PlanAsync(TempRepository repository, string? upstream) =>
        (await new RebaseService(_git).GetCommitsToRebaseAsync(repository.Path, upstream))
            .Select(commit => new RebaseStep(commit, RebaseAction.Pick))
            .ToList();

    private static async Task<string[]> SubjectsAsync(TempRepository repository) =>
        [.. (await repository.RunAsync("log", "--format=%s")).OutputLines];

    [Fact]
    public async Task GetCommits_ListsThemOldestFirst_WithFullMessages()
    {
        var (repository, @base) = await FourCommitsAsync();
        using var _ = repository;
        await repository.CommitFileAsync("five.txt", "5", "five\n\nwith a body");

        var commits = await new RebaseService(_git).GetCommitsToRebaseAsync(repository.Path, @base);

        Assert.Equal(["one", "two", "three", "four", "five"], commits.Select(commit => commit.Subject));
        Assert.Equal("five\n\nwith a body", commits[^1].Message);
        Assert.Equal(6, (await new RebaseService(_git).GetCommitsToRebaseAsync(repository.Path, null)).Count);
    }

    [Fact]
    public async Task Plan_Reorders_Drops_AndRewords()
    {
        var (repository, @base) = await FourCommitsAsync();
        using var _ = repository;
        var plan = await PlanAsync(repository, @base);

        // four, one ("one, reworded" with a body), three; "two" dropped.
        var reordered = new List<RebaseStep>
        {
            plan[3],
            plan[0] with { Action = RebaseAction.Reword, NewMessage = "one, reworded\n\nWith \"quotes\" and a body" },
            plan[1] with { Action = RebaseAction.Drop },
            plan[2],
        };

        var outcome = await new RebaseService(_git).RebaseInteractiveAsync(repository.Path, @base, reordered, autoStash: false);

        Assert.Equal(RebaseOutcome.Done, outcome);
        Assert.Equal(["three", "one, reworded", "four", "base"], await SubjectsAsync(repository));
        Assert.Equal("one, reworded\n\nWith \"quotes\" and a body", (await repository.RunAsync("log", "-1", "--format=%B", "HEAD~1")).StandardOutput.Trim());
        Assert.False(File.Exists(Path.Combine(repository.Path, "two.txt")));
    }

    [Fact]
    public async Task Squash_CombinesMessages_AndFixup_KeepsTheOneAbove()
    {
        var (repository, @base) = await FourCommitsAsync();
        using var _ = repository;
        var plan = await PlanAsync(repository, @base);

        plan[1] = plan[1] with { Action = RebaseAction.Squash };
        plan[3] = plan[3] with { Action = RebaseAction.Fixup };

        Assert.Equal(RebaseOutcome.Done, await new RebaseService(_git).RebaseInteractiveAsync(repository.Path, @base, plan, autoStash: false));

        Assert.Equal(["three", "one", "base"], await SubjectsAsync(repository));
        Assert.Equal("one\n\ntwo", (await repository.RunAsync("log", "-1", "--format=%B", "HEAD~1")).StandardOutput.Trim());
        Assert.Equal("three", (await repository.RunAsync("log", "-1", "--format=%B")).StandardOutput.Trim());

        // The squashed and fixed-up changes are all there.
        var files = (await repository.RunAsync("ls-files")).OutputLines;
        Assert.Equal(["base.txt", "four.txt", "one.txt", "three.txt", "two.txt"], files.Order());
    }

    [Fact]
    public async Task Edit_StopsThere_AndStagedChangesAreAmendedOnContinue()
    {
        var (repository, @base) = await FourCommitsAsync();
        using var _ = repository;
        var plan = await PlanAsync(repository, @base);
        plan[1] = plan[1] with { Action = RebaseAction.Edit };

        Assert.Equal(RebaseOutcome.Stopped, await new RebaseService(_git).RebaseInteractiveAsync(repository.Path, @base, plan, autoStash: false));
        var state = await new OperationService(_git).GetStateAsync(repository.Path);
        Assert.Equal(OperationKind.Rebase, state.Kind);
        Assert.True(state.IsEditStop);
        Assert.False(state.CanSkip);

        await File.WriteAllTextAsync(Path.Combine(repository.Path, "two.txt"), "two, amended\n");
        await repository.RunAsync("add", "two.txt");
        Assert.Equal(ContinueOutcome.Done, await new OperationService(_git).ContinueAsync(repository.Path, OperationKind.Rebase));

        Assert.Equal(["four", "three", "two", "one", "base"], await SubjectsAsync(repository));
        Assert.Equal("two, amended\n", (await repository.RunAsync("show", "HEAD~2:two.txt")).StandardOutput);
    }

    [Fact]
    public async Task Reorder_ThatConflicts_StopsLikeAnyRebase_AndTheRewordStillApplies()
    {
        using var repository = await TempRepository.CreateAsync();
        await repository.RunAsync("config", "core.autocrlf", "false");
        await repository.CommitFileAsync("f.txt", "base\n", "base");
        var @base = (await repository.RunAsync("rev-parse", "HEAD")).StandardOutput.Trim();
        await repository.CommitFileAsync("f.txt", "first\n", "first");
        await repository.CommitFileAsync("f.txt", "second\n", "second");
        var plan = await PlanAsync(repository, @base);

        // "second" first: it no longer applies on "base".
        var swapped = new List<RebaseStep> { plan[1] with { Action = RebaseAction.Reword, NewMessage = "second, reworded" }, plan[0] };
        Assert.Equal(RebaseOutcome.Conflict, await new RebaseService(_git).RebaseInteractiveAsync(repository.Path, @base, swapped, autoStash: false));
        Assert.False((await new OperationService(_git).GetStateAsync(repository.Path)).IsEditStop);

        await File.WriteAllTextAsync(Path.Combine(repository.Path, "f.txt"), "second\n");
        await repository.RunAsync("add", "f.txt");
        var operations = new OperationService(_git);
        var outcome = await operations.ContinueAsync(repository.Path, OperationKind.Rebase);
        if (outcome == ContinueOutcome.StoppedAgain)
        {
            // "first" now conflicts with "second": keep "first".
            await File.WriteAllTextAsync(Path.Combine(repository.Path, "f.txt"), "first\n");
            await repository.RunAsync("add", "f.txt");
            outcome = await operations.ContinueAsync(repository.Path, OperationKind.Rebase);
        }

        Assert.Equal(ContinueOutcome.Done, outcome);
        Assert.Equal(["first", "second, reworded", "base"], await SubjectsAsync(repository));
    }

    [Fact]
    public async Task AutoStash_BringsBackUncommittedWork_IncludingNewFiles()
    {
        var (repository, @base) = await FourCommitsAsync();
        using var _ = repository;
        await File.WriteAllTextAsync(Path.Combine(repository.Path, "base.txt"), "edited\n");
        await File.WriteAllTextAsync(Path.Combine(repository.Path, "notes.txt"), "untracked\n");
        var plan = await PlanAsync(repository, @base);
        plan[0] = plan[0] with { Action = RebaseAction.Drop };

        Assert.Equal(RebaseOutcome.Done, await new RebaseService(_git).RebaseInteractiveAsync(repository.Path, @base, plan, autoStash: true));

        Assert.Equal("edited\n", await File.ReadAllTextAsync(Path.Combine(repository.Path, "base.txt")));
        Assert.Equal("untracked\n", await File.ReadAllTextAsync(Path.Combine(repository.Path, "notes.txt")));
        Assert.Equal(["four", "three", "two", "base"], await SubjectsAsync(repository));
    }

    [Fact]
    public async Task FromTheFirstCommit_UsesRoot()
    {
        var (repository, _) = await FourCommitsAsync();
        using var __ = repository;
        var first = (await repository.RunAsync("rev-list", "--max-parents=0", "HEAD")).StandardOutput.Trim();
        var rebase = new RebaseService(_git);

        Assert.Null(await rebase.GetUpstreamForAsync(repository.Path, first));
        var plan = await PlanAsync(repository, null);
        plan[1] = plan[1] with { Action = RebaseAction.Fixup }; // "one" into "base"

        Assert.Equal(RebaseOutcome.Done, await rebase.RebaseInteractiveAsync(repository.Path, null, plan, autoStash: false));
        Assert.Equal(["four", "three", "two", "base"], await SubjectsAsync(repository));
    }

    [Fact]
    public void BuildTodo_RefusesASquashWithNothingAbove()
    {
        var commit = new RebaseCommit("abc", "abc", "one", "one");
        var plan = new[] { new RebaseStep(commit, RebaseAction.Drop), new RebaseStep(commit with { Sha = "def" }, RebaseAction.Squash) };

        Assert.Throws<ArgumentException>(() => RebaseService.BuildTodo(plan, Path.GetTempPath()));
    }

    [Fact]
    public async Task GetUpstreamFor_RefusesCommitsFromOtherBranches()
    {
        var (repository, _) = await FourCommitsAsync();
        using var __ = repository;
        await repository.RunAsync("checkout", "-q", "-b", "other");
        await repository.CommitFileAsync("other.txt", "other", "other");
        var other = (await repository.RunAsync("rev-parse", "HEAD")).StandardOutput.Trim();
        await repository.RunAsync("checkout", "-q", "main");

        await Assert.ThrowsAsync<InvalidOperationException>(() => new RebaseService(_git).GetUpstreamForAsync(repository.Path, other));
    }
}
