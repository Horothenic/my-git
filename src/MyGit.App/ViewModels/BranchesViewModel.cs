using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyGit.App.Services;
using MyGit.Core.Branches;
using MyGit.Core.Git;
using MyGit.Core.History;

namespace MyGit.App.ViewModels;

/// <summary>A local branch row in the Branch dropdown. Its commands call back into <see cref="BranchesViewModel"/>.</summary>
public sealed partial class LocalBranchItemViewModel(LocalBranch branch, BranchesViewModel owner)
{
    public LocalBranch Branch { get; } = branch;

    [RelayCommand]
    private Task SwitchAsync() => owner.SwitchAsync(this);

    [RelayCommand]
    private Task FastForwardAsync() => owner.FastForwardAsync(this);

    [RelayCommand]
    private Task RenameAsync() => owner.RenameAsync(this);

    [RelayCommand]
    private Task DeleteAsync() => owner.DeleteAsync(this);

    [RelayCommand]
    private void RebaseOnto() => owner.RequestRebase(this);

    [RelayCommand]
    private void RebaseInteractivelyOnto() => owner.RequestInteractiveRebase(this);

    [RelayCommand]
    private void StackOnTop() => owner.RequestStackOnTop();

    /// <summary>e.g. "Interactively rebase feature/inventory onto main…" (set when the list loads).</summary>
    public string InteractiveRebaseText { get; init; } = string.Empty;

    /// <summary>e.g. "Rebase feature/inventory onto main…" (set when the list loads).</summary>
    public string RebaseText { get; init; } = string.Empty;

    public string Name => Branch.Name;

    public bool IsCurrent => Branch.IsCurrent;

    public bool IsNotCurrent => !IsCurrent;

    public bool CanFastForward => Branch.CanFastForward;

    public string FastForwardText => $"Fast-forward {Name} (↓{Branch.Behind})";

    /// <summary>The branch's pull request on GitHub, if any (set when the list loads).</summary>
    public MyGit.GitHub.PullRequests.PullRequestInfo? PullRequest { get; init; }

    public bool HasPullRequest => PullRequest is not null;

    /// <summary>e.g. "#12 open", "#9 merged · can delete".</summary>
    public string PullRequestBadge => PullRequest is null ? string.Empty
        : PullRequest.State == MyGit.GitHub.PullRequests.PullRequestState.Merged && !IsCurrent ? $"{PullRequest.Badge} · can delete"
        : PullRequest.Badge;

    public bool IsPullRequestOpen => PullRequest?.State == MyGit.GitHub.PullRequests.PullRequestState.Open;

    public bool IsPullRequestDraft => PullRequest?.State == MyGit.GitHub.PullRequests.PullRequestState.Draft;

    public bool IsPullRequestMerged => PullRequest?.State == MyGit.GitHub.PullRequests.PullRequestState.Merged;

    public bool IsPullRequestClosed => PullRequest?.State == MyGit.GitHub.PullRequests.PullRequestState.Closed;

    /// <summary>"↑1 ↓4", "not published", "deleted on GitHub", or "" when in sync.</summary>
    public string TrackingText =>
        Branch.Upstream is null ? "not published" :
        Branch.UpstreamGone ? "deleted on GitHub" :
        string.Join(" ", new[] { Branch.Ahead > 0 ? $"↑{Branch.Ahead}" : null, Branch.Behind > 0 ? $"↓{Branch.Behind}" : null }.Where(part => part is not null));
}

/// <summary>A remote branch nobody tracks here yet.</summary>
public sealed partial class RemoteBranchItemViewModel(RemoteBranch branch, BranchesViewModel owner)
{
    public RemoteBranch Branch { get; } = branch;

    [RelayCommand]
    private Task CheckoutAsync() => owner.CheckoutRemoteAsync(this);

    public string Name => Branch.Name;
}

/// <summary>
/// The Branch dropdown in the toolbar: filter, local and remote branches, and their actions.
/// </summary>
public sealed partial class BranchesViewModel : ObservableObject
{
    private readonly BranchService _branches;
    private readonly HistoryService _history;
    private readonly IDialogService _dialogs;
    private BranchList _all = new([], []);
    private string? _repositoryPath;

    public BranchesViewModel(BranchService branches, HistoryService history, IDialogService dialogs)
    {
        _branches = branches;
        _history = history;
        _dialogs = dialogs;
    }

    /// <summary>Raised with an error for the message bar.</summary>
    public event Action<string>? MessageRaised;

    /// <summary>Raised after the branch changed or branches were edited, so the window refreshes.</summary>
    public event Func<Task>? RepositoryChanged;

    /// <summary>Raised to rebase the current branch onto a branch: (what to rebase onto, its name).</summary>
    public event Func<string, string, Task>? RebaseRequested;

    public void RequestRebase(LocalBranchItemViewModel item)
    {
        CloseRequested?.Invoke();
        _ = RebaseRequested?.Invoke(item.Name, item.Name);
    }

    /// <summary>Raised to rebase the current branch interactively onto a branch (its name).</summary>
    public event Func<string, Task>? InteractiveRebaseRequested;

    public void RequestInteractiveRebase(LocalBranchItemViewModel item)
    {
        CloseRequested?.Invoke();
        _ = InteractiveRebaseRequested?.Invoke(item.Name);
    }

    /// <summary>Raised so the view can close the dropdown before a dialog or after switching.</summary>
    public event Action? CloseRequested;

    public ObservableCollection<LocalBranchItemViewModel> Local { get; } = [];

    public ObservableCollection<RemoteBranchItemViewModel> Remote { get; } = [];

    public bool HasRemote => Remote.Count > 0;

    [ObservableProperty]
    public partial string FilterText { get; set; } = string.Empty;

    /// <summary>Call when the dropdown opens.</summary>
    public async Task LoadAsync(string? repositoryPath)
    {
        _repositoryPath = repositoryPath;
        FilterText = string.Empty;

        if (repositoryPath is null)
        {
            _all = new BranchList([], []);
            ApplyFilter();
            return;
        }

        try
        {
            _all = await _branches.ListAsync(repositoryPath);
        }
        catch (GitCommandException exception)
        {
            MessageRaised?.Invoke(exception.Message);
            _all = new BranchList([], []);
        }

        ApplyFilter();
    }

    partial void OnFilterTextChanged(string value) => ApplyFilter();

    public Task SwitchAsync(LocalBranchItemViewModel item) =>
        item.IsCurrent ? Task.CompletedTask : SwitchWithAsync(item.Name, stash => _branches.SwitchAsync(_repositoryPath!, item.Name, stash));

    /// <summary>Switches to a local branch by name (asks to stash when needed, like the dropdown).</summary>
    public Task SwitchToAsync(string branch) =>
        SwitchWithAsync(branch, stash => _branches.SwitchAsync(_repositoryPath!, branch, stash));

    /// <summary>Raised to stack a new branch on top of the checked-out one (the Stacks tab does it).</summary>
    public event Func<Task>? StackOnTopRequested;

    public void RequestStackOnTop()
    {
        CloseRequested?.Invoke();
        _ = StackOnTopRequested?.Invoke();
    }

    public Task CheckoutRemoteAsync(RemoteBranchItemViewModel item) =>
        SwitchWithAsync(item.Branch.LocalName, stash => _branches.CheckoutRemoteAsync(_repositoryPath!, item.Branch, stash));

    [RelayCommand]
    private async Task NewBranchAsync()
    {
        if (_repositoryPath is not { } repository)
        {
            return;
        }

        CloseRequested?.Invoke();
        var current = _all.Local.FirstOrDefault(b => b.IsCurrent)?.Name ?? "the current commit";

        var answer = await _dialogs.AskNameAsync(new NameDialogOptions(
            "New branch",
            $"Starts from {current}. Your uncommitted changes come along.",
            "feature/my-change",
            "Create branch",
            name => _history.ValidateBranchNameAsync(repository, name),
            CheckBoxText: "Switch to the new branch",
            CheckBoxDefault: true));

        if (answer is not null)
        {
            await RunAsync(() => _history.CreateBranchAsync(repository, answer.Name, "HEAD", answer.IsChecked));
        }
    }

    public async Task FastForwardAsync(LocalBranchItemViewModel item)
    {
        if (_repositoryPath is not { } repository)
        {
            return;
        }

        CloseRequested?.Invoke();
        await RunAsync(async () =>
        {
            if (await _branches.FastForwardAsync(repository, item.Branch) == FastForwardOutcome.Diverged)
            {
                MessageRaised?.Invoke($"{item.Name} has commits of its own, so it can't just move forward. Switch to it and pull instead.");
            }
        });
    }

    public async Task RenameAsync(LocalBranchItemViewModel item)
    {
        if (_repositoryPath is not { } repository)
        {
            return;
        }

        CloseRequested?.Invoke();
        var note = item.Branch.Upstream is { } upstream
            ? $"Renames it here only. It stays linked to {upstream} on GitHub."
            : "Renames it here only.";

        var answer = await _dialogs.AskNameAsync(new NameDialogOptions(
            $"Rename {item.Name}",
            note,
            item.Name,
            "Rename",
            name => _history.ValidateBranchNameAsync(repository, name)));

        if (answer is not null)
        {
            await RunAsync(() => _branches.RenameAsync(repository, item.Name, answer.Name));
        }
    }

    public async Task DeleteAsync(LocalBranchItemViewModel item)
    {
        if (_repositoryPath is not { } repository || item.IsCurrent)
        {
            return;
        }

        CloseRequested?.Invoke();
        if (!await _dialogs.ConfirmAsync(
                $"Delete branch {item.Name}?",
                "This deletes it here only, not on GitHub.",
                "Delete"))
        {
            return;
        }

        await RunAsync(async () =>
        {
            if (await _branches.DeleteAsync(repository, item.Name) != DeleteOutcome.NotMerged)
            {
                return;
            }

            if (await _dialogs.ConfirmAsync(
                    $"{item.Name} has commits that aren't anywhere else",
                    "They're not merged into another branch or pushed. Deleting the branch loses them " +
                    "(they can still be recovered for a while with git reflog).",
                    "Delete anyway"))
            {
                await _branches.DeleteAsync(repository, item.Name, force: true);
            }
        });
    }

    private async Task SwitchWithAsync(string target, Func<bool, Task<SwitchOutcome>> switchTo)
    {
        if (_repositoryPath is null)
        {
            return;
        }

        CloseRequested?.Invoke();

        await RunAsync(async () =>
        {
            var outcome = await switchTo(false);

            if (outcome == SwitchOutcome.NeedsStash)
            {
                var choice = await _dialogs.ChooseAsync(
                    $"Your uncommitted changes would be lost by switching to {target}",
                    "Stash them, switch, and bring them back on the new branch. If they clash with that branch, they stay safe in the stash.",
                    ["Stash and switch", "Cancel"]);

                if (choice != 0)
                {
                    return;
                }

                outcome = await switchTo(true);
            }

            if (outcome == SwitchOutcome.StashConflict)
            {
                MessageRaised?.Invoke($"Switched to {target}. Your uncommitted changes clash with it, so they stayed safe in the stash " +
                                      "(git stash list). Switch back to get them with \"git stash pop\".");
            }
        });
    }

    private IReadOnlyDictionary<string, MyGit.GitHub.PullRequests.PullRequestInfo> _pullRequests =
        new Dictionary<string, MyGit.GitHub.PullRequests.PullRequestInfo>();

    /// <summary>Shows each branch's pull request (after GitHub was asked).</summary>
    public void ApplyPullRequests(IReadOnlyDictionary<string, MyGit.GitHub.PullRequests.PullRequestInfo> pullRequests)
    {
        _pullRequests = pullRequests;
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        Local.Clear();
        Remote.Clear();

        foreach (var branch in _all.Local.Where(b => Matches(b.Name)))
        {
            var current = _all.Local.FirstOrDefault(b => b.IsCurrent)?.Name;
            Local.Add(new LocalBranchItemViewModel(branch, this) 
            {
                RebaseText = current is null ? "Rebase onto this…" : $"Rebase {current} onto {branch.Name}…",
                InteractiveRebaseText = current is null ? "Interactively rebase onto this…" : $"Interactively rebase {current} onto {branch.Name}…",
                PullRequest = _pullRequests.GetValueOrDefault(branch.Name),
            });
        }

        foreach (var branch in _all.Remote.Where(b => Matches(b.Name)))
        {
            Remote.Add(new RemoteBranchItemViewModel(branch, this));
        }

        OnPropertyChanged(nameof(HasRemote));
    }

    private bool Matches(string name) => FilterText.Length == 0 || name.Contains(FilterText, StringComparison.OrdinalIgnoreCase);

    private async Task RunAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception exception) when (exception is GitCommandException or InvalidOperationException)
        {
            var error = exception is GitCommandException git ? git.Result.StandardError.Trim() : string.Empty;
            MessageRaised?.Invoke(error.Length > 0 ? error.Split('\n')[^1] : exception.Message);
        }

        if (RepositoryChanged is { } handler)
        {
            await handler();
        }
    }
}
