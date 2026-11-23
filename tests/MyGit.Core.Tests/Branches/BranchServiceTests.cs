using MyGit.Core.Branches;
using MyGit.Core.Git;

namespace MyGit.Core.Tests.Branches;

/// <summary>A bare repository playing GitHub, "me" (the app's side) and "teammate".</summary>
public sealed class BranchServiceTests : IAsyncLifetime
{
    private readonly GitRunner _git = new();
    private readonly BranchService _branches;
    private string _root = string.Empty;

    public BranchServiceTests()
    {
        _branches = new BranchService(_git);
    }

    private string Remote => Path.Combine(_root, "remote.git");

    private string Me => Path.Combine(_root, "me");

    private string Teammate => Path.Combine(_root, "teammate");

    public async Task InitializeAsync()
    {
        _root = TempRepository.CreateEmptyFolder();
        await Git(_root, "init", "--bare", "-b", "main", Remote);

        foreach (var clone in new[] { Me, Teammate })
        {
            await Git(_root, "clone", "-q", Remote, clone);
            await Git(clone, "config", "user.name", "Test");
            await Git(clone, "config", "user.email", "test@example.com");
            await Git(clone, "config", "commit.gpgsign", "false");
        }

        await CommitAsync(Me, "readme.md", "hello", "first");
        await Git(Me, "push", "-q", "-u", "origin", "main");
        await Git(Teammate, "pull", "-q", "origin", "main");
        await Git(Teammate, "branch", "--set-upstream-to=origin/main", "main");
    }

    public Task DisposeAsync()
    {
        TempRepository.DeleteFolder(_root);
        return Task.CompletedTask;
    }

    [Theory]
    [InlineData("ahead 1, behind 4", 1, 4, false)]
    [InlineData("behind 2", 0, 2, false)]
    [InlineData("gone", 0, 0, true)]
    [InlineData("", 0, 0, false)]
    public void ParseTrack_ReadsGitsTrackingText(string text, int ahead, int behind, bool gone)
    {
        Assert.Equal((ahead, behind, gone), BranchService.ParseTrack(text));
    }

    [Fact]
    public async Task List_ShowsLocalWithCounts_AndUntrackedRemoteBranches()
    {
        await Git(Teammate, "checkout", "-q", "-b", "fix-login");
        await CommitAsync(Teammate, "login.txt", "x", "fix login");
        await Git(Teammate, "push", "-q", "-u", "origin", "fix-login");
        await Git(Teammate, "checkout", "-q", "main");
        await CommitAsync(Teammate, "a.txt", "a", "teammate on main");
        await Git(Teammate, "push", "-q");
        await Git(Me, "checkout", "-q", "-b", "experiment");
        await Git(Me, "fetch", "-q");

        var list = await _branches.ListAsync(Me);

        var current = list.Local[0];
        Assert.Equal(("experiment", true, (string?)null), (current.Name, current.IsCurrent, current.Upstream));

        var main = list.Local.Single(b => b.Name == "main");
        Assert.Equal(("origin/main", 0, 1), (main.Upstream, main.Ahead, main.Behind));
        Assert.True(main.CanFastForward);

        var remote = Assert.Single(list.Remote);
        Assert.Equal(("origin/fix-login", "fix-login"), (remote.Name, remote.LocalName));
    }

    [Fact]
    public async Task FastForward_UpdatesABranchYouAreNotOn()
    {
        await CommitAsync(Teammate, "a.txt", "a", "teammate on main");
        await Git(Teammate, "push", "-q");
        await Git(Me, "checkout", "-q", "-b", "feature");
        await Git(Me, "fetch", "-q");
        var main = (await _branches.ListAsync(Me)).Local.Single(b => b.Name == "main");

        Assert.Equal(FastForwardOutcome.Done, await _branches.FastForwardAsync(Me, main));

        var updated = (await _branches.ListAsync(Me)).Local.Single(b => b.Name == "main");
        Assert.Equal(0, updated.Behind);
        Assert.Equal("feature", (await Git(Me, "branch", "--show-current")).StandardOutput.Trim()); // still on feature
    }

    [Fact]
    public async Task FastForward_RefusesWhenTheBranchHasItsOwnCommits()
    {
        await CommitAsync(Teammate, "a.txt", "a", "teammate on main");
        await Git(Teammate, "push", "-q");
        await CommitAsync(Me, "mine.txt", "m", "mine on main");
        await Git(Me, "checkout", "-q", "-b", "feature");
        await Git(Me, "fetch", "-q");
        var main = (await _branches.ListAsync(Me)).Local.Single(b => b.Name == "main");

        Assert.False(main.CanFastForward);
        Assert.Equal(FastForwardOutcome.Diverged, await _branches.FastForwardAsync(Me, main));
    }

    [Fact]
    public async Task Switch_AsksToStash_ThenCarriesTheChangesOver()
    {
        await Git(Me, "checkout", "-q", "-b", "other");
        await CommitAsync(Me, "readme.md", "other version", "other edit");
        await Git(Me, "checkout", "-q", "main");
        await File.WriteAllTextAsync(Path.Combine(Me, "notes.txt"), "untracked notes");
        await File.WriteAllTextAsync(Path.Combine(Me, "readme.md"), "my uncommitted edit");

        Assert.Equal(SwitchOutcome.NeedsStash, await _branches.SwitchAsync(Me, "other"));
        Assert.Equal("main", (await Git(Me, "branch", "--show-current")).StandardOutput.Trim());

        var outcome = await _branches.SwitchAsync(Me, "other", stash: true);

        // Same file changed on both sides: the branch switch works, the stash can't go back cleanly.
        Assert.Equal(SwitchOutcome.StashConflict, outcome);
        Assert.Equal("other", (await Git(Me, "branch", "--show-current")).StandardOutput.Trim());
        Assert.NotEmpty((await Git(Me, "stash", "list")).OutputLines); // nothing lost
    }

    [Fact]
    public async Task Switch_WithStash_BringsChangesBack_WhenTheyDontClash()
    {
        await Git(Me, "branch", "other");
        await File.WriteAllTextAsync(Path.Combine(Me, "notes.txt"), "untracked notes");

        Assert.Equal(SwitchOutcome.Done, await _branches.SwitchAsync(Me, "other", stash: true));
        Assert.Equal("untracked notes", await File.ReadAllTextAsync(Path.Combine(Me, "notes.txt")));
        Assert.Empty((await Git(Me, "stash", "list")).OutputLines);
    }

    [Fact]
    public async Task CheckoutRemote_CreatesATrackingBranch()
    {
        await Git(Teammate, "checkout", "-q", "-b", "fix-login");
        await CommitAsync(Teammate, "login.txt", "x", "fix login");
        await Git(Teammate, "push", "-q", "-u", "origin", "fix-login");
        await Git(Me, "fetch", "-q");
        var remote = Assert.Single((await _branches.ListAsync(Me)).Remote);

        Assert.Equal(SwitchOutcome.Done, await _branches.CheckoutRemoteAsync(Me, remote));

        var list = await _branches.ListAsync(Me);
        Assert.Equal(("fix-login", "origin/fix-login"), (list.Local[0].Name, list.Local[0].Upstream));
        Assert.Empty(list.Remote);
    }

    [Fact]
    public async Task RenameAndDelete()
    {
        await Git(Me, "branch", "old-name");
        await _branches.RenameAsync(Me, "old-name", "new-name");
        Assert.Contains((await _branches.ListAsync(Me)).Local, b => b.Name == "new-name");

        Assert.Equal(DeleteOutcome.Done, await _branches.DeleteAsync(Me, "new-name"));

        await Git(Me, "checkout", "-q", "-b", "unmerged");
        await CommitAsync(Me, "x.txt", "x", "only here");
        await Git(Me, "checkout", "-q", "main");

        Assert.Equal(DeleteOutcome.NotMerged, await _branches.DeleteAsync(Me, "unmerged"));
        Assert.Equal(DeleteOutcome.Done, await _branches.DeleteAsync(Me, "unmerged", force: true));
        Assert.DoesNotContain((await _branches.ListAsync(Me)).Local, b => b.Name == "unmerged");
    }

    private async Task CommitAsync(string repository, string file, string content, string message)
    {
        await File.WriteAllTextAsync(Path.Combine(repository, file), content);
        await Git(repository, "add", file);
        await Git(repository, "commit", "-q", "-m", message);
    }

    private Task<GitResult> Git(string directory, params string[] arguments) => _git.RunCheckedAsync(directory, arguments);
}
