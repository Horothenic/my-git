using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyGit.App.Services;
using MyGit.App.ViewModels.Rebase;
using MyGit.Core.Git;
using MyGit.Core.Operations;

namespace MyGit.App.ViewModels;

/// <summary>
/// The status bar shown while a rebase, merge, cherry-pick or revert is stopped
/// (Continue / Skip / Abort), and starting a rebase.
/// </summary>
public sealed partial class OperationViewModel : ObservableObject
{
    private readonly OperationService _operations;
    private readonly RebaseService _rebase;
    private readonly IDialogService _dialogs;
    private string? _repositoryPath;

    public OperationViewModel(OperationService operations, RebaseService rebase, IDialogService dialogs)
    {
        _operations = operations;
        _rebase = rebase;
        _dialogs = dialogs;
    }

    /// <summary>Raised with an error or explanation for the message bar.</summary>
    public event Action<string>? MessageRaised;

    /// <summary>Raised with a short success note.</summary>
    public event Action<string>? NoticeRaised;

    /// <summary>Raised after the operation moved on, so the window refreshes.</summary>
    public event Func<Task>? RepositoryChanged;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsActive), nameof(Title), nameof(Details), nameof(CanSkip))]
    [NotifyCanExecuteChangedFor(nameof(ContinueCommand), nameof(SkipCommand), nameof(AbortCommand))]
    public partial OperationState State { get; set; } = OperationState.Idle;

    /// <summary>How many files still have conflicts (set by the window from the Changes tab).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Details), nameof(CanContinue))]
    [NotifyCanExecuteChangedFor(nameof(ContinueCommand))]
    public partial int ConflictCount { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ContinueCommand), nameof(SkipCommand), nameof(AbortCommand))]
    public partial bool IsBusy { get; set; }

    public bool IsActive => State.IsActive;

    public bool CanSkip => State.CanSkip;

    public bool CanContinue => IsActive && ConflictCount == 0 && !IsBusy;

    public string Title => State.Kind switch
    {
        OperationKind.Rebase => $"Rebasing {State.Branch ?? "your branch"} onto {State.Onto ?? "another branch"}",
        OperationKind.Merge => "Merging",
        OperationKind.CherryPick => "Cherry-picking",
        OperationKind.Revert => "Reverting",
        _ => string.Empty,
    };

    public string Details
    {
        get
        {
            var parts = new List<string>();

            if (State.IsEditStop)
            {
                var subject = State.CurrentCommit is { Length: > 0 } stoppedAt ? $"“{stoppedAt}”" : "this commit";
                return $"stopped at {subject} so you can change it: stage changes to add them to it, then Continue";
            }

            if (State.Kind == OperationKind.Rebase && State.TotalSteps > 0)
            {
                parts.Add($"commit {State.Step} of {State.TotalSteps}");
            }

            if (State.CurrentCommit is { Length: > 0 } commit)
            {
                parts.Add($"“{commit}”");
            }

            parts.Add(ConflictCount switch
            {
                0 => "conflicts resolved, ready to continue",
                1 => "1 conflict left",
                _ => $"{ConflictCount} conflicts left",
            });

            return string.Join(" · ", parts);
        }
    }

    public async Task RefreshAsync(string? repositoryPath)
    {
        _repositoryPath = repositoryPath;

        try
        {
            State = repositoryPath is null ? OperationState.Idle : await _operations.GetStateAsync(repositoryPath);
        }
        catch (GitCommandException)
        {
            State = OperationState.Idle;
        }
    }

    /// <summary>
    /// Rebase the current branch onto <paramref name="onto"/> (a branch name or commit id), after confirming.
    /// </summary>
    public async Task StartRebaseAsync(string branch, string onto, string ontoLabel)
    {
        if (_repositoryPath is not { } repository || IsActive)
        {
            return;
        }

        var commits = await _rebase.CountCommitsToReplayAsync(repository, onto);
        var what = commits switch { 0 => "your branch (nothing new to replay)", 1 => "your 1 commit", _ => $"your {commits} commits" };

        var (confirmed, autoStash) = await _dialogs.ConfirmWithOptionAsync(
            $"Rebase {branch} onto {ontoLabel}?",
            $"Replays {what} on top of {ontoLabel}. Their ids change, so if {branch} is already pushed, the next push has to be a force push. " +
            "If something goes wrong, Abort puts everything back.",
            "Rebase",
            "Stash my uncommitted changes (new files too) and bring them back after",
            optionDefault: true,
            dangerous: false);

        if (!confirmed)
        {
            return;
        }

        await RunAsync(async () =>
        {
            switch (await _rebase.RebaseAsync(repository, onto, autoStash))
            {
                case RebaseOutcome.Done:
                    NoticeRaised?.Invoke($"Rebased {branch} onto {ontoLabel}");
                    break;
                case RebaseOutcome.UpToDate:
                    NoticeRaised?.Invoke($"{branch} already contains {ontoLabel}: nothing to rebase");
                    break;
                case RebaseOutcome.Conflict:
                    MessageRaised?.Invoke("The rebase stopped on a conflict. Resolve the files under Conflicts in the Changes tab, then press Continue.");
                    break;
            }
        });
    }

    /// <summary>Interactive rebase of the current branch onto another branch (its commits that aren't on it).</summary>
    public Task StartInteractiveRebaseAsync(string branch, string onto) => InteractiveRebaseAsync(branch, onto, $"{branch} onto {onto}");

    /// <summary>Interactive rebase of the current branch from <paramref name="commit"/> (that commit and the ones after it).</summary>
    public async Task StartInteractiveRebaseFromAsync(string branch, string commit, string commitLabel)
    {
        if (_repositoryPath is not { } repository || IsActive)
        {
            return;
        }

        string? upstream;
        try
        {
            upstream = await _rebase.GetUpstreamForAsync(repository, commit);
        }
        catch (InvalidOperationException exception)
        {
            MessageRaised?.Invoke(exception.Message);
            return;
        }

        await InteractiveRebaseAsync(branch, upstream, $"{branch} from {commitLabel}");
    }

    private async Task InteractiveRebaseAsync(string branch, string? upstream, string description)
    {
        if (_repositoryPath is not { } repository || IsActive)
        {
            return;
        }

        IReadOnlyList<RebaseCommit> commits;
        bool flattensMerges;
        try
        {
            commits = await _rebase.GetCommitsToRebaseAsync(repository, upstream);
            flattensMerges = await _rebase.HasMergesToFlattenAsync(repository, upstream);
        }
        catch (GitCommandException exception)
        {
            MessageRaised?.Invoke(exception.Result.StandardError.Trim());
            return;
        }

        if (commits.Count == 0)
        {
            NoticeRaised?.Invoke($"Nothing to rebase: {branch} has no commits of its own there");
            return;
        }

        var window = new InteractiveRebaseViewModel(description, commits, flattensMerges);
        if (!await _dialogs.ShowInteractiveRebaseAsync(window))
        {
            return;
        }

        await RunAsync(async () => Report(await _rebase.RebaseInteractiveAsync(repository, upstream, window.Plan, window.AutoStash), branch));
    }

    private void Report(RebaseOutcome outcome, string branch)
    {
        switch (outcome)
        {
            case RebaseOutcome.Done:
            case RebaseOutcome.UpToDate:
                NoticeRaised?.Invoke($"Rebased {branch}");
                break;
            case RebaseOutcome.Stopped:
                break; // the status bar says it stopped to change the commit, and what to do
            case RebaseOutcome.Conflict:
                MessageRaised?.Invoke("The rebase stopped on a conflict. Resolve the files under Conflicts in the Changes tab, then press Continue.");
                break;
        }
    }

    [RelayCommand(CanExecute = nameof(CanContinue))]
    private Task ContinueAsync() => StepAsync(kind => _operations.ContinueAsync(_repositoryPath!, kind));

    [RelayCommand(CanExecute = nameof(CanSkipNow))]
    private async Task SkipAsync()
    {
        var commit = State.CurrentCommit is { } subject ? $"“{subject}”" : "this commit";
        if (await _dialogs.ConfirmAsync($"Skip {commit}?", "Its changes are dropped from the result. The rest carries on.", "Skip commit"))
        {
            await StepAsync(kind => _operations.SkipAsync(_repositoryPath!, kind));
        }
    }

    [RelayCommand(CanExecute = nameof(CanAbortNow))]
    private async Task AbortAsync()
    {
        if (_repositoryPath is not { } repository ||
            !await _dialogs.ConfirmAsync($"Abort {Title.ToLowerInvariant()}?", "Everything goes back to how it was before it started.", "Abort"))
        {
            return;
        }

        var kind = State.Kind;
        await RunAsync(async () =>
        {
            await _operations.AbortAsync(repository, kind);
            await RestoreStashAsync(repository);
            NoticeRaised?.Invoke("Aborted. Everything is back to how it was.");
        });
    }

    private bool CanSkipNow() => IsActive && CanSkip && !IsBusy;

    private bool CanAbortNow() => IsActive && !IsBusy;

    private async Task StepAsync(Func<OperationKind, Task<ContinueOutcome>> step)
    {
        if (_repositoryPath is not { } repository)
        {
            return;
        }

        var kind = State.Kind;
        await RunAsync(async () =>
        {
            if (await step(kind) == ContinueOutcome.StoppedAgain)
            {
                // An edit stop is explained by the status bar itself; a conflict needs a pointer to the files.
                if (!(await _operations.GetStateAsync(repository)).IsEditStop)
                {
                    MessageRaised?.Invoke("Stopped on another conflict. Resolve the files under Conflicts, then press Continue.");
                }

                return;
            }

            await RestoreStashAsync(repository);
            NoticeRaised?.Invoke(kind == OperationKind.Rebase ? "Rebase finished" : "Done");
        });
    }

    private async Task RestoreStashAsync(string repository)
    {
        if (!await _rebase.RestorePendingStashAsync(repository))
        {
            MessageRaised?.Invoke("Your uncommitted changes clash with the result, so they stayed in the stash (Stashes, in the Changes tab).");
        }
    }

    private async Task RunAsync(Func<Task> action)
    {
        IsBusy = true;

        try
        {
            await action();
        }
        catch (GitCommandException exception)
        {
            var error = exception.Result.StandardError.Trim();
            MessageRaised?.Invoke(error.Length > 0 ? error.Split('\n')[^1] : exception.Message);
        }
        finally
        {
            IsBusy = false;
        }

        if (RepositoryChanged is { } handler)
        {
            await handler();
        }
    }
}
