using MyGit.Core.Git;
using MyGit.Core.Sync;

namespace MyGit.Core.Tests.Sync;

/// <summary>
/// Each test gets a bare repository playing GitHub, plus two clones: "me" (the app's side)
/// and "teammate" (someone else pushing).
/// </summary>
public sealed class SyncServiceTests : IAsyncLifetime
{
    private readonly GitRunner _git = new();
    private readonly SyncService _sync;
    private string _root = string.Empty;

    public SyncServiceTests()
    {
        _sync = new SyncService(_git);
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

    [Fact]
    public async Task Tracking_CountsAheadAndBehind_AfterFetch()
    {
        await CommitAsync(Teammate, "a.txt", "a", "teammate 1");
        await CommitAsync(Teammate, "b.txt", "b", "teammate 2");
        await Git(Teammate, "push", "-q");
        await CommitAsync(Me, "mine.txt", "m", "mine");

        Assert.Equal((1, 0), await AheadBehindAsync()); // before fetch we don't know about theirs

        await _sync.FetchAsync(Me);

        var tracking = await _sync.GetTrackingAsync(Me);
        Assert.Equal("main", tracking.Branch);
        Assert.Equal("origin/main", tracking.Upstream);
        Assert.Equal((1, 2), (tracking.Ahead, tracking.Behind));
    }

    [Fact]
    public async Task Pull_Rebase_PutsMyCommitOnTopAndKeepsUncommittedChanges()
    {
        await CommitAsync(Teammate, "a.txt", "a", "teammate");
        await Git(Teammate, "push", "-q");
        await CommitAsync(Me, "mine.txt", "m", "mine");
        await File.WriteAllTextAsync(Path.Combine(Me, "readme.md"), "work in progress");

        var result = await _sync.PullAsync(Me, PullMode.Rebase);

        Assert.Equal(PullOutcome.Done, result.Outcome);
        Assert.Equal(1, result.CommitsPulled);
        var log = await Git(Me, "log", "--format=%s");
        Assert.Equal(["mine", "teammate", "first"], log.OutputLines); // straight line, mine on top
        Assert.Equal("work in progress", await File.ReadAllTextAsync(Path.Combine(Me, "readme.md")));
    }

    [Fact]
    public async Task Pull_FastForwardOnly_RefusesWhenDiverged()
    {
        await CommitAsync(Teammate, "a.txt", "a", "teammate");
        await Git(Teammate, "push", "-q");
        await CommitAsync(Me, "mine.txt", "m", "mine");

        var result = await _sync.PullAsync(Me, PullMode.FastForwardOnly);

        Assert.Equal(PullOutcome.NotFastForward, result.Outcome);
    }

    [Fact]
    public async Task Pull_ReportsConflict()
    {
        await CommitAsync(Teammate, "readme.md", "theirs", "teammate edit");
        await Git(Teammate, "push", "-q");
        await CommitAsync(Me, "readme.md", "mine", "my edit");

        var result = await _sync.PullAsync(Me, PullMode.Rebase);

        Assert.Equal(PullOutcome.Conflict, result.Outcome);
        await Git(Me, "rebase", "--abort");
    }

    [Fact]
    public async Task Push_SendsCommits_AndCountsThem()
    {
        await CommitAsync(Me, "a.txt", "a", "one");
        await CommitAsync(Me, "b.txt", "b", "two");

        var result = await _sync.PushAsync(Me);

        Assert.Equal(PushOutcome.Done, result.Outcome);
        Assert.Equal(2, result.CommitsPushed);
        Assert.Equal((0, 0), await AheadBehindAsync());
    }

    [Fact]
    public async Task Push_IsRejected_WhenTheRemoteHasNewCommits()
    {
        await CommitAsync(Teammate, "a.txt", "a", "teammate");
        await Git(Teammate, "push", "-q");
        await CommitAsync(Me, "mine.txt", "m", "mine");

        var result = await _sync.PushAsync(Me);

        Assert.Equal(PushOutcome.Rejected, result.Outcome);
    }

    [Fact]
    public async Task ForcePush_WithLease_RefusesUnfetchedWork_ButWorksAfterFetch()
    {
        await CommitAsync(Teammate, "a.txt", "a", "teammate");
        await Git(Teammate, "push", "-q");
        await Git(Me, "commit", "--allow-empty", "-q", "-m", "mine");

        // The lease protects the teammate's commit we haven't seen yet.
        Assert.Equal(PushOutcome.LeaseRejected, (await _sync.PushAsync(Me, force: true)).Outcome);

        await _sync.FetchAsync(Me);
        Assert.Equal(PushOutcome.Done, (await _sync.PushAsync(Me, force: true)).Outcome);
    }

    [Fact]
    public async Task Push_PublishesANewBranch()
    {
        await Git(Me, "checkout", "-q", "-b", "feature/slots");
        await CommitAsync(Me, "slots.txt", "s", "slots");

        var before = await _sync.GetTrackingAsync(Me);
        Assert.True(before.NeedsPublish);

        var result = await _sync.PushAsync(Me);

        Assert.True(result.Published);
        Assert.Equal(1, result.CommitsPushed);
        Assert.Equal("origin/feature/slots", result.Upstream);
        Assert.False((await _sync.GetTrackingAsync(Me)).NeedsPublish);
    }

    private async Task<(int Ahead, int Behind)> AheadBehindAsync()
    {
        var tracking = await _sync.GetTrackingAsync(Me);
        return (tracking.Ahead, tracking.Behind);
    }

    private async Task CommitAsync(string repository, string file, string content, string message)
    {
        await File.WriteAllTextAsync(Path.Combine(repository, file), content);
        await Git(repository, "add", file);
        await Git(repository, "commit", "-q", "-m", message);
    }

    private Task<GitResult> Git(string directory, params string[] arguments) => _git.RunCheckedAsync(directory, arguments);
}
