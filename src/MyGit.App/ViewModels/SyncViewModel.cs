using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyGit.App.Services;
using MyGit.Core.Git;
using MyGit.Core.Sync;

namespace MyGit.App.ViewModels;

/// <summary>What the sync buttons are doing right now.</summary>
public enum SyncOperation
{
    None,
    Fetch,
    Pull,
    Push,
}

/// <summary>
/// The Fetch, Pull and Push buttons in the toolbar: counts, tooltips, and the actions behind them.
/// Counts come from local refs and only change on Fetch, Pull, Push, or switching branch / repo.
/// </summary>
public sealed partial class SyncViewModel : ObservableObject
{
    private static readonly TimeSpan NoticeDuration = TimeSpan.FromSeconds(6);

    private readonly SyncService _sync;
    private readonly IDialogService _dialogs;
    private CancellationTokenSource? _noticeTimer;
    private string? _repositoryPath;

    public SyncViewModel(SyncService sync, IDialogService dialogs)
    {
        _sync = sync;
        _dialogs = dialogs;
    }

    /// <summary>Raised with an error for the yellow message bar.</summary>
    public event Action<string>? MessageRaised;

    /// <summary>Raised after a pull or push changed the repository, so the rest of the window refreshes.</summary>
    public event Func<Task>? RepositoryChanged;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CommitsToPull), nameof(CommitsToPush), nameof(HasCommitsToPull), nameof(HasCommitsToPush),
        nameof(NeedsPublish), nameof(IsPushVisible), nameof(CanPull), nameof(CanPush), nameof(CanFetch), nameof(PullToolTip), nameof(PushToolTip))]
    [NotifyCanExecuteChangedFor(nameof(FetchCommand), nameof(PullCommand), nameof(PullWithMergeCommand), nameof(PullFastForwardOnlyCommand),
        nameof(PushCommand), nameof(ForcePushCommand))]
    public partial TrackingInfo Tracking { get; set; } = TrackingInfo.None;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFetching), nameof(IsPulling), nameof(IsPushing), nameof(IsBusy), nameof(CanPull), nameof(CanPush), nameof(CanFetch))]
    [NotifyCanExecuteChangedFor(nameof(FetchCommand), nameof(PullCommand), nameof(PullWithMergeCommand), nameof(PullFastForwardOnlyCommand),
        nameof(PushCommand), nameof(ForcePushCommand))]
    public partial SyncOperation Running { get; set; }

    public int CommitsToPull => Tracking.Behind;

    public int CommitsToPush => Tracking.Ahead;

    public bool HasCommitsToPull => CommitsToPull > 0;

    public bool HasCommitsToPush => CommitsToPush > 0;

    /// <summary>The branch isn't on GitHub yet: the push button turns into Publish.</summary>
    public bool NeedsPublish => Tracking.NeedsPublish;

    public bool IsPushVisible => !NeedsPublish;

    public bool IsFetching => Running == SyncOperation.Fetch;

    public bool IsPulling => Running == SyncOperation.Pull;

    public bool IsPushing => Running == SyncOperation.Push;

    public bool IsBusy => Running != SyncOperation.None;

    public bool CanFetch => !IsBusy && _repositoryPath is not null && Tracking.HasRemote;

    public bool CanPull => !IsBusy && Tracking.Upstream is not null;

    public bool CanPush => !IsBusy && !Tracking.IsDetached && (Tracking.Upstream is not null || Tracking.NeedsPublish);

    [ObservableProperty]
    public partial string FetchToolTip { get; set; } = "Fetch from GitHub";

    public string PullToolTip => Tracking.Upstream is null
        ? "This branch isn't on GitHub yet"
        : CommitsToPull == 0
            ? $"Pull from {Tracking.Upstream} (nothing new since your last fetch). Right-click for merge or fast-forward."
            : $"Pull {Plural(CommitsToPull)} from {Tracking.Upstream} with rebase. Right-click for merge or fast-forward.";

    public string PushToolTip => Tracking.IsDetached
        ? "You're not on a branch"
        : NeedsPublish
            ? $"Publish {Tracking.Branch} to GitHub"
            : CommitsToPush == 0
                ? $"Nothing to push to {Tracking.Upstream}. Right-click to force push."
                : $"Push {Plural(CommitsToPush)} to {Tracking.Upstream}. Right-click to force push.";

    /// <summary>Short green note after an action, e.g. "Pulled 3 commits from origin/main".</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotice))]
    public partial string? Notice { get; set; }

    public bool HasNotice => Notice is not null;

    /// <summary>Re-reads the counts from local refs (no network). Call on open, focus and branch change.</summary>
    public async Task RefreshAsync(string? repositoryPath)
    {
        _repositoryPath = repositoryPath;

        if (repositoryPath is null)
        {
            Tracking = TrackingInfo.None;
            return;
        }

        try
        {
            Tracking = await _sync.GetTrackingAsync(repositoryPath);
            FetchToolTip = DescribeLastFetch(repositoryPath);
        }
        catch (GitCommandException)
        {
            Tracking = TrackingInfo.None;
        }

        OnPropertyChanged(nameof(CanFetch));
        FetchCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanFetch))]
    private Task FetchAsync() => RunAsync(SyncOperation.Fetch, async repository =>
    {
        await _sync.FetchAsync(repository);
        await RefreshAsync(repository);

        ShowNotice(Tracking.Upstream is null
            ? "Fetched from GitHub"
            : CommitsToPull == 0 ? $"Up to date with {Tracking.Upstream}" : $"{Plural(CommitsToPull)} to pull from {Tracking.Upstream}");
    });

    [RelayCommand(CanExecute = nameof(CanPull))]
    private Task PullAsync() => PullWithAsync(PullMode.Rebase);

    [RelayCommand(CanExecute = nameof(CanPull))]
    private Task PullWithMergeAsync() => PullWithAsync(PullMode.Merge);

    [RelayCommand(CanExecute = nameof(CanPull))]
    private Task PullFastForwardOnlyAsync() => PullWithAsync(PullMode.FastForwardOnly);

    [RelayCommand(CanExecute = nameof(CanPush))]
    private Task PushAsync() => PushWithAsync(force: false);

    [RelayCommand(CanExecute = nameof(CanPush))]
    private async Task ForcePushAsync()
    {
        var target = Tracking.Upstream ?? "GitHub";
        if (await _dialogs.ConfirmAsync(
                $"Force push to {target}?",
                $"Your version of {Tracking.Branch} replaces the one on GitHub. Commits that are only on GitHub will be dropped. " +
                "If someone pushed since your last fetch, the push is refused, so their work is safe.",
                "Force push"))
        {
            await PushWithAsync(force: true);
        }
    }

    private Task PullWithAsync(PullMode mode) => RunAsync(SyncOperation.Pull, async repository =>
    {
        var result = await _sync.PullAsync(repository, mode);
        await RefreshAsync(repository);

        switch (result.Outcome)
        {
            case PullOutcome.Done:
                ShowNotice(result.CommitsPulled == 0 ? $"Already up to date with {result.Upstream}" : $"Pulled {Plural(result.CommitsPulled)} from {result.Upstream}");
                break;
            case PullOutcome.StashConflict:
                MessageRaised?.Invoke($"Pulled from {result.Upstream}, but putting your uncommitted changes back conflicted. " +
                                      "Your changes are safe in the stash (git stash list).");
                break;
            case PullOutcome.Conflict:
                MessageRaised?.Invoke($"The pull from {result.Upstream} stopped on a conflict. Resolve it in your editor, then run " +
                                      "\"git rebase --continue\" (or \"git rebase --abort\" to undo the pull). A conflict screen comes in a later version.");
                break;
            case PullOutcome.NotFastForward:
                MessageRaised?.Invoke($"Can't fast-forward: you and {result.Upstream} both have new commits. Pull with rebase or merge instead.");
                break;
            case PullOutcome.NoUpstream:
                MessageRaised?.Invoke("This branch isn't on GitHub yet, so there's nothing to pull.");
                break;
        }

        await NotifyChangedAsync();
    });

    private Task PushWithAsync(bool force) => RunAsync(SyncOperation.Push, async repository =>
    {
        var result = await _sync.PushAsync(repository, force);
        await RefreshAsync(repository);

        switch (result.Outcome)
        {
            case PushOutcome.Done when result.Published:
                ShowNotice($"Published {Tracking.Branch} to {result.Upstream}");
                break;
            case PushOutcome.Done:
                ShowNotice(result.CommitsPushed == 0 ? $"Nothing to push to {result.Upstream}" :
                    $"{(force ? "Force pushed" : "Pushed")} {Plural(result.CommitsPushed)} to {result.Upstream}");
                break;
            case PushOutcome.LeaseRejected:
                MessageRaised?.Invoke($"Force push refused: {result.Upstream} has commits you haven't fetched yet. Fetch and check them first.");
                break;
            case PushOutcome.Rejected:
                await AskAfterRejectedPushAsync(result.Upstream);
                return;
        }

        await NotifyChangedAsync();
    });

    /// <summary>GitHub has commits you don't: pull then push (the normal fix), force push, or cancel.</summary>
    private async Task AskAfterRejectedPushAsync(string upstream)
    {
        // Running is still Push here; let the follow-up actions run.
        Running = SyncOperation.None;
        await RefreshAsync(_repositoryPath);

        var newCommits = CommitsToPull > 0 ? Plural(CommitsToPull) : "new commits";
        var choice = await _dialogs.ChooseAsync(
            $"{upstream} has {newCommits} you don't have",
            "Pull them first, then push. Force push replaces what's on GitHub with your version, dropping those commits.",
            ["Pull, then push", "Force push…", "Cancel"],
            dangerIndex: 1);

        if (choice == 0)
        {
            await PullWithAsync(PullMode.Rebase);
            if (Tracking.Behind == 0)
            {
                await PushWithAsync(force: false);
            }
        }
        else if (choice == 1)
        {
            await ForcePushAsync();
        }
    }

    private async Task RunAsync(SyncOperation operation, Func<string, Task> action)
    {
        if (_repositoryPath is not { } repository || IsBusy)
        {
            return;
        }

        Running = operation;
        Notice = null;

        try
        {
            await action(repository);
        }
        catch (Exception exception) when (exception is GitCommandException or InvalidOperationException)
        {
            MessageRaised?.Invoke(Describe(exception));
            await RefreshAsync(repository);
        }
        finally
        {
            Running = SyncOperation.None;
        }
    }

    private async Task NotifyChangedAsync()
    {
        if (RepositoryChanged is { } handler)
        {
            await handler();
        }
    }

    /// <summary>Shows a short green note under the toolbar (also used by Undo).</summary>
    public void ShowNotice(string text)
    {
        _noticeTimer?.Cancel();
        _noticeTimer = new CancellationTokenSource();
        var token = _noticeTimer.Token;

        Notice = text;
        _ = HideNoticeLaterAsync(token);
    }

    private async Task HideNoticeLaterAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(NoticeDuration, token);
            Notice = null;
        }
        catch (OperationCanceledException)
        {
            // A newer notice replaced this one.
        }
    }

    private static string Describe(Exception exception)
    {
        if (exception is GitCommandException git)
        {
            var error = git.Result.StandardError;

            if (error.Contains("could not read Username", StringComparison.OrdinalIgnoreCase) ||
                error.Contains("Authentication failed", StringComparison.OrdinalIgnoreCase))
            {
                return "GitHub asked for a login. Sign in with the account button at the top right, then try again.";
            }

            if (error.Contains("Could not resolve host", StringComparison.OrdinalIgnoreCase) ||
                error.Contains("unable to access", StringComparison.OrdinalIgnoreCase))
            {
                return "Couldn't reach GitHub. Check your connection and try again.";
            }

            var lastLine = error.Split('\n', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim();
            if (!string.IsNullOrEmpty(lastLine))
            {
                return lastLine;
            }
        }

        return exception.Message;
    }

    private static string DescribeLastFetch(string repositoryPath)
    {
        // git updates .git/FETCH_HEAD on every fetch (and on every pull, which fetches first).
        var fetchHead = Path.Combine(repositoryPath, ".git", "FETCH_HEAD");
        if (!File.Exists(fetchHead))
        {
            return "Fetch from GitHub (not fetched yet)";
        }

        var age = DateTime.Now - File.GetLastWriteTime(fetchHead);
        var when = age switch
        {
            { TotalMinutes: < 1 } => "just now",
            { TotalMinutes: < 60 } => $"{(int)age.TotalMinutes} min ago",
            { TotalHours: < 24 } => $"{(int)age.TotalHours} h ago",
            _ => $"{(int)age.TotalDays} days ago",
        };

        return $"Fetch from GitHub (last fetched {when})";
    }

    private static string Plural(int commits) => commits == 1 ? "1 commit" : $"{commits} commits";
}
