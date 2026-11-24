using MyGit.Core.Branches;
using MyGit.Core.Git;
using MyGit.Core.Operations;
using MyGit.Core.Stacks;

namespace MyGit.Core.Tests.Stacks;

/// <summary>
/// A bare repository plays GitHub. "me" has a pushed stack main → model → ui → save; "github" is a second clone
/// that squash-merges PRs into main the way GitHub's button does (one new commit, the branch deleted).
/// </summary>
public sealed class StackSyncTests : IAsyncLifetime
{
    private readonly GitRunner _git = new();
    private readonly string _root = TempRepository.CreateEmptyFolder();

    private string Remote => Path.Combine(_root, "remote.git");

    private string Me => Path.Combine(_root, "me");

    private string GitHub => Path.Combine(_root, "github");

    private StackService Stacks => new(_git, new RebaseService(_git));

    public async Task InitializeAsync()
    {
        await _git.RunCheckedAsync(_root, ["init", "--bare", "-q", "-b", "main", Remote]);
        foreach (var clone in new[] { Me, GitHub })
        {
            await _git.RunCheckedAsync(_root, ["clone", "-q", Remote, clone]);
            await Git(clone, "config", "user.name", "Test");
            await Git(clone, "config", "user.email", "test@example.com");
            await Git(clone, "config", "commit.gpgsign", "false");
            await Git(clone, "config", "core.autocrlf", "false");
        }

        await CommitAsync(Me, "base.txt", "base\n", "base");
        await Git(Me, "push", "-q", "-u", "origin", "main");

        foreach (var name in new[] { "model", "ui", "save" })
        {
            await Stacks.CreateOnTopAsync(Me, name, "main");
            await CommitAsync(Me, $"{name}.txt", $"{name}\n", $"{name} work");
        }

        await Git(Me, "push", "-q", "-u", "origin", "model", "ui", "save");
    }

    public Task DisposeAsync()
    {
        TempRepository.DeleteFolder(_root);
        return Task.CompletedTask;
    }

    private Task<GitResult> Git(string folder, params string[] arguments) => _git.RunCheckedAsync(folder, arguments);

    private async Task CommitAsync(string folder, string file, string content, string message)
    {
        await File.WriteAllTextAsync(Path.Combine(folder, file), content);
        await Git(folder, "add", file);
        await Git(folder, "commit", "-q", "-m", message);
    }

    /// <summary>What GitHub's "Squash and merge" does: one new commit on main, then the branch is deleted.</summary>
    private async Task SquashMergeOnGitHubAsync(string branch, string intoBranch = "main")
    {
        await Git(GitHub, "fetch", "-q", "origin");
        await Git(GitHub, "switch", "-q", intoBranch);
        await Git(GitHub, "reset", "-q", "--hard", $"origin/{intoBranch}");
        await Git(GitHub, "merge", "-q", "--squash", $"origin/{branch}");
        await Git(GitHub, "commit", "-q", "-m", $"{branch} (squashed, #1)");
        await Git(GitHub, "push", "-q", "origin", intoBranch);
        await Git(GitHub, "push", "-q", "origin", "--delete", branch);
    }

    private async Task<string[]> LogAsync(string branch) => [.. (await Git(Me, "log", "--format=%s", branch)).OutputLines];

    [Fact]
    public async Task AfterTheBottomIsSquashMerged_TheRestGoesOntoMain_WithoutItsCommits()
    {
        await SquashMergeOnGitHubAsync("model");
        var stack = (await Stacks.GetStacksAsync(Me)).Single();

        var outcome = await Stacks.SyncMergedAsync(Me, stack, mergedCount: 1, returnTo: "save", autoStash: true);

        Assert.Equal(RebaseOutcome.Done, outcome);
        Assert.Equal(["save work", "ui work", "model (squashed, #1)", "base"], await LogAsync("save"));
        Assert.Equal(["ui work", "model (squashed, #1)", "base"], await LogAsync("ui"));
        Assert.Equal(["model (squashed, #1)", "base"], await LogAsync("main")); // fast-forwarded

        var synced = (await Stacks.GetStacksAsync(Me)).Single();
        Assert.Equal(["ui", "save"], synced.Branches.Select(branch => branch.Name));
        Assert.Equal("main", synced.Branches[0].Parent);
        Assert.False(synced.NeedsRestack);
        Assert.False((await _git.RunAsync(Me, ["rev-parse", "--verify", "--quiet", "refs/heads/model"])).Succeeded);
        Assert.Equal("save", (await Git(Me, "branch", "--show-current")).StandardOutput.Trim());
    }

    [Fact]
    public async Task StandingOnTheMergedBranch_EndsOnTheNewBottom()
    {
        await Git(Me, "switch", "-q", "model");
        await SquashMergeOnGitHubAsync("model");
        var stack = (await Stacks.GetStacksAsync(Me)).Single();

        await Stacks.SyncMergedAsync(Me, stack, mergedCount: 1, returnTo: "model", autoStash: true);

        Assert.Equal("ui", (await Git(Me, "branch", "--show-current")).StandardOutput.Trim());
    }

    [Fact]
    public async Task TwoMergedAtOnce_AreBothRemoved()
    {
        await SquashMergeOnGitHubAsync("model");

        // GitHub retargets ui's PR to main once model is gone; then ui (still carrying model's commits) is squashed too.
        await SquashMergeOnGitHubAsync("ui");
        var stack = (await Stacks.GetStacksAsync(Me)).Single();

        Assert.Equal(RebaseOutcome.Done, await Stacks.SyncMergedAsync(Me, stack, mergedCount: 2, returnTo: "save", autoStash: false));

        Assert.Equal(["save work", "ui (squashed, #1)", "model (squashed, #1)", "base"], await LogAsync("save"));
        Assert.Equal(["save"], (await Stacks.GetStacksAsync(Me)).Single().Branches.Select(branch => branch.Name));
    }

    [Fact]
    public async Task TheWholeStackMerged_JustCleansUp()
    {
        await Git(Me, "switch", "-q", "main");
        await SquashMergeOnGitHubAsync("model");
        await SquashMergeOnGitHubAsync("ui");
        await SquashMergeOnGitHubAsync("save");
        var stack = (await Stacks.GetStacksAsync(Me)).Single();

        Assert.Equal(RebaseOutcome.UpToDate, await Stacks.SyncMergedAsync(Me, stack, mergedCount: 3, returnTo: "main", autoStash: false));

        Assert.Empty(await Stacks.GetStacksAsync(Me));
        Assert.Equal(["save (squashed, #1)", "ui (squashed, #1)", "model (squashed, #1)", "base"], await LogAsync("main"));
    }

    [Fact]
    public async Task LocalCommitsOnMain_StopTheSync_BeforeAnythingChanges()
    {
        await SquashMergeOnGitHubAsync("model");
        await Git(Me, "switch", "-q", "main");
        await CommitAsync(Me, "local.txt", "local\n", "only on my main");
        await Git(Me, "switch", "-q", "save");
        var stack = (await Stacks.GetStacksAsync(Me)).Single();

        await Assert.ThrowsAsync<InvalidOperationException>(() => Stacks.SyncMergedAsync(Me, stack, 1, "save", autoStash: false));

        Assert.True((await _git.RunAsync(Me, ["rev-parse", "--verify", "--quiet", "refs/heads/model"])).Succeeded);
        Assert.Equal(3, (await Stacks.GetStacksAsync(Me)).Single().Branches.Count);
    }

    [Fact]
    public async Task Rename_KeepsTheBranchAboveStackedOnIt()
    {
        await new BranchService(_git).RenameAsync(Me, "ui", "inventory-ui");

        var stack = (await Stacks.GetStacksAsync(Me)).Single();
        Assert.Equal(["model", "inventory-ui", "save"], stack.Branches.Select(branch => branch.Name));
        Assert.Equal("inventory-ui", stack.Branches[2].Parent);
        Assert.False(stack.NeedsRestack);
    }

    [Fact]
    public void StackDescription_StrikesThroughMergedPullRequests()
    {
        var section = StackDescription.Section("main", [14, 15, 16], current: 1, merged: new HashSet<int> { 14 });

        Assert.Contains("~~#14~~ → **#15** → #16", section);
    }
}
