using MyGit.Core.Changes;
using MyGit.Core.Git;
using MyGit.Core.Operations;

namespace MyGit.Core.Tests.Operations;

public sealed class RebaseAndConflictTests
{
    private readonly GitRunner _git = new();

    /// <summary>
    /// main: shared → "main edit" (file.txt = "main")
    /// feature: shared → "feature edit" (file.txt = "feature") → "feature extra" (extra.txt)
    /// Rebasing feature onto main conflicts on file.txt in the first commit.
    /// </summary>
    private static async Task<TempRepository> ConflictingBranchesAsync()
    {
        var repository = await TempRepository.CreateAsync();
        await repository.RunAsync("config", "core.autocrlf", "false");
        await repository.CommitFileAsync("file.txt", "shared\n", "shared");
        await repository.RunAsync("checkout", "-q", "-b", "feature");
        await repository.CommitFileAsync("file.txt", "feature\n", "feature edit");
        await repository.CommitFileAsync("extra.txt", "extra\n", "feature extra");
        await repository.RunAsync("checkout", "-q", "main");
        await repository.CommitFileAsync("file.txt", "main\n", "main edit");
        await repository.RunAsync("checkout", "-q", "feature");
        return repository;
    }

    [Fact]
    public async Task Rebase_StopsOnConflict_AndTheStateDescribesIt()
    {
        using var repository = await ConflictingBranchesAsync();

        Assert.Equal(2, await new RebaseService(_git).CountCommitsToReplayAsync(repository.Path, "main"));
        Assert.Equal(RebaseOutcome.Conflict, await new RebaseService(_git).RebaseAsync(repository.Path, "main", autoStash: false));

        var state = await new OperationService(_git).GetStateAsync(repository.Path);
        Assert.Equal(OperationKind.Rebase, state.Kind);
        Assert.Equal(("feature", "main"), (state.Branch, state.Onto));
        Assert.Equal((1, 2), (state.Step, state.TotalSteps));
        Assert.Equal("feature edit", state.CurrentCommit);

        var status = await new ChangesService(_git).GetStatusAsync(repository.Path);
        Assert.Equal(ChangeKind.Conflicted, Assert.Single(status.Unstaged, c => c.Path == "file.txt").Kind);
    }

    [Fact]
    public async Task UseYours_ThenContinue_FinishesTheRebase()
    {
        using var repository = await ConflictingBranchesAsync();
        await new RebaseService(_git).RebaseAsync(repository.Path, "main", autoStash: false);

        // During a rebase, "theirs" is your commit being replayed.
        await new ConflictService(_git).UseSideAsync(repository.Path, "file.txt", ConflictSide.Theirs);
        var outcome = await new OperationService(_git).ContinueAsync(repository.Path, OperationKind.Rebase);

        Assert.Equal(ContinueOutcome.Done, outcome);
        Assert.False((await new OperationService(_git).GetStateAsync(repository.Path)).IsActive);
        Assert.Equal("feature\n", await File.ReadAllTextAsync(Path.Combine(repository.Path, "file.txt")));
        var log = await repository.RunAsync("log", "--format=%s");
        Assert.Equal(["feature extra", "feature edit", "main edit", "shared"], log.OutputLines);
    }

    [Fact]
    public async Task Skip_DropsTheConflictingCommit()
    {
        using var repository = await ConflictingBranchesAsync();
        await new RebaseService(_git).RebaseAsync(repository.Path, "main", autoStash: false);

        Assert.Equal(ContinueOutcome.Done, await new OperationService(_git).SkipAsync(repository.Path, OperationKind.Rebase));

        var log = await repository.RunAsync("log", "--format=%s");
        Assert.Equal(["feature extra", "main edit", "shared"], log.OutputLines);
    }

    [Fact]
    public async Task Abort_PutsEverythingBack_AndAutoStashBringsBackNewFiles()
    {
        using var repository = await ConflictingBranchesAsync();
        var before = (await repository.RunAsync("rev-parse", "HEAD")).StandardOutput.Trim();
        await File.WriteAllTextAsync(Path.Combine(repository.Path, "notes.txt"), "untracked work");
        var rebase = new RebaseService(_git);

        Assert.Equal(RebaseOutcome.Conflict, await rebase.RebaseAsync(repository.Path, "main", autoStash: true));
        Assert.False(File.Exists(Path.Combine(repository.Path, "notes.txt"))); // stashed while the rebase runs
        Assert.True(await rebase.HasPendingStashAsync(repository.Path));

        await new OperationService(_git).AbortAsync(repository.Path, OperationKind.Rebase);
        Assert.True(await rebase.RestorePendingStashAsync(repository.Path));

        Assert.Equal(before, (await repository.RunAsync("rev-parse", "HEAD")).StandardOutput.Trim());
        Assert.Equal("untracked work", await File.ReadAllTextAsync(Path.Combine(repository.Path, "notes.txt")));
        Assert.False(await rebase.HasPendingStashAsync(repository.Path));
    }

    [Fact]
    public async Task Rebase_WithoutConflicts_KeepsNewFiles_WithAutoStash()
    {
        using var repository = await TempRepository.CreateAsync();
        await repository.CommitFileAsync("a.txt", "a", "first");
        await repository.RunAsync("checkout", "-q", "-b", "feature");
        await repository.CommitFileAsync("b.txt", "b", "feature work");
        await repository.RunAsync("checkout", "-q", "main");
        await repository.CommitFileAsync("c.txt", "c", "main work");
        await repository.RunAsync("checkout", "-q", "feature");
        await File.WriteAllTextAsync(Path.Combine(repository.Path, "notes.txt"), "untracked work");

        Assert.Equal(RebaseOutcome.Done, await new RebaseService(_git).RebaseAsync(repository.Path, "main", autoStash: true));

        Assert.Equal("untracked work", await File.ReadAllTextAsync(Path.Combine(repository.Path, "notes.txt")));
        Assert.True(File.Exists(Path.Combine(repository.Path, "c.txt")));
    }

    [Fact]
    public async Task MarkResolved_AfterFixingTheFileByHand()
    {
        using var repository = await ConflictingBranchesAsync();
        await new RebaseService(_git).RebaseAsync(repository.Path, "main", autoStash: false);
        var file = Path.Combine(repository.Path, "file.txt");
        Assert.True(ConflictService.HasConflictMarkers(file));

        await File.WriteAllTextAsync(file, "main and feature\n");
        await new ConflictService(_git).MarkResolvedAsync(repository.Path, "file.txt");

        var status = await new ChangesService(_git).GetStatusAsync(repository.Path);
        Assert.DoesNotContain(status.Unstaged, c => c.Kind == ChangeKind.Conflicted);
        await new OperationService(_git).AbortAsync(repository.Path, OperationKind.Rebase);
    }

    [Fact]
    public async Task MergeTool_ThatSavesAResult_ResolvesTheFile()
    {
        using var repository = await ConflictingBranchesAsync();
        await new RebaseService(_git).RebaseAsync(repository.Path, "main", autoStash: false);

        // A stand-in "tool" run by git's shell: takes your version as the result.
        var tool = new MergeTool("copy yours", "cp \"$REMOTE\" \"$MERGED\"");
        var outcome = await new MergeToolService(_git).RunAsync(repository.Path, "file.txt", tool);

        Assert.Equal(MergeToolOutcome.Resolved, outcome);
        Assert.Equal("feature\n", await File.ReadAllTextAsync(Path.Combine(repository.Path, "file.txt")));
        Assert.Empty((await repository.RunAsync("ls-files", "-u")).OutputLines);
        await new OperationService(_git).AbortAsync(repository.Path, OperationKind.Rebase);
    }

    [Fact]
    public async Task MergeTool_ThatLeavesMarkers_KeepsTheFileConflicted()
    {
        using var repository = await ConflictingBranchesAsync();
        await new RebaseService(_git).RebaseAsync(repository.Path, "main", autoStash: false);

        var lazyTool = new MergeTool("does nothing", "true");
        var outcome = await new MergeToolService(_git).RunAsync(repository.Path, "file.txt", lazyTool);

        Assert.Equal(MergeToolOutcome.NotResolved, outcome);
        Assert.NotEmpty((await repository.RunAsync("ls-files", "-u", "--", "file.txt")).OutputLines);
        await new OperationService(_git).AbortAsync(repository.Path, OperationKind.Rebase);
    }

    [Fact]
    public async Task UnityYamlMerge_IsOnlyOfferedForUnityAssets()
    {
        using var repository = await TempRepository.CreateAsync();
        var tools = new MergeToolService(_git);

        var forCode = await tools.GetToolsForAsync(repository.Path, "Assets/Scripts/Inventory.cs");
        Assert.DoesNotContain(forCode, tool => tool.Name == "UnityYAMLMerge");

        // On a machine with Unity installed it's offered (first) for prefabs; elsewhere it's simply absent.
        var forPrefab = await tools.GetToolsForAsync(repository.Path, "Assets/Prefabs/Bag.prefab");
        if (forPrefab.Any(tool => tool.Name == "UnityYAMLMerge"))
        {
            Assert.Equal("UnityYAMLMerge", forPrefab[0].Name);
        }
    }

    [Fact]
    public void CommandFor_UsesEachToolsArgumentOrder()
    {
        Assert.Equal(
            "\"C:/Unity/UnityYAMLMerge.exe\" merge -p \"$BASE\" \"$REMOTE\" \"$LOCAL\" \"$MERGED\"",
            MergeToolService.CommandFor("UnityYAMLMerge", @"C:\Unity\UnityYAMLMerge.exe"));
        Assert.StartsWith("\"C:/Code/Code.exe\" --wait --merge", MergeToolService.CommandFor("VS Code", @"C:\Code\Code.exe"));
    }
}
