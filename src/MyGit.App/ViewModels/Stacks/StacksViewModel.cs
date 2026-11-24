using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyGit.App.Services;
using MyGit.App.ViewModels.PullRequests;
using MyGit.Core.Branches;
using MyGit.Core.Git;
using MyGit.Core.History;
using MyGit.Core.Operations;
using MyGit.Core.PullRequests;
using MyGit.Core.Stacks;
using MyGit.GitHub.PullRequests;

namespace MyGit.App.ViewModels.Stacks;

/// <summary>A stack in the list on the left. Kept across refreshes (only <see cref="Stack"/> changes), so the selection stays.</summary>
public sealed partial class StackItemViewModel(Stack stack) : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Name), nameof(Summary))]
    public partial Stack Stack { get; set; } = stack;

    public string Name => Stack.Name;

    public string Summary => $"{Stack.Branches.Count} branches · {(Stack.NeedsRestack ? "needs restack" : "up to date")}";
}

/// <summary>One branch card in the selected stack (shown top first).</summary>
public sealed partial class StackBranchViewModel(StackEntry entry, LocalBranch? branch, PullRequestInfo? pullRequest, bool isCurrent, StacksViewModel owner)
{
    public StackEntry Entry { get; } = entry;

    public string Name => Entry.Name;

    public bool IsCurrent { get; } = isCurrent;

    public bool IsNotCurrent => !IsCurrent;

    public PullRequestInfo? PullRequest { get; } = pullRequest;

    public bool HasPullRequest => PullRequest is not null;

    public bool HasNoPullRequest => PullRequest is null;

    public string PullRequestBadge => PullRequest?.Badge ?? string.Empty;

    public bool IsPullRequestOpen => PullRequest?.State == PullRequestState.Open;

    public bool IsPullRequestDraft => PullRequest?.State == PullRequestState.Draft;

    public bool IsPullRequestMerged => PullRequest?.State == PullRequestState.Merged;

    public bool IsPullRequestClosed => PullRequest?.State == PullRequestState.Closed;

    public bool NeedsRestack => Entry.NeedsRestack;

    public string RestackText => $"needs restack: {Entry.Parent} changed";

    /// <summary>e.g. "2 commits · ↑1 to push", "1 commit · not pushed".</summary>
    public string Details
    {
        get
        {
            var commits = Entry.CommitCount == 1 ? "1 commit" : $"{Entry.CommitCount} commits";
            var push = branch?.Upstream is null ? "not pushed"
                : branch.UpstreamGone ? "deleted on GitHub"
                : branch.Ahead > 0 && branch.Behind > 0 ? $"↑{branch.Ahead} ↓{branch.Behind}: rewritten, push stack to update GitHub"
                : branch.Ahead > 0 ? $"↑{branch.Ahead} to push"
                : branch.Behind > 0 ? $"↓{branch.Behind} on GitHub"
                : "pushed";
            return $"{commits} · {push}";
        }
    }

    /// <summary>The branch needs pushing (new, or different from GitHub).</summary>
    public bool NeedsPush => branch?.Upstream is null || branch.UpstreamGone || branch.Ahead > 0 || branch.Behind > 0;

    [RelayCommand]
    private Task SwitchAsync() => owner.SwitchToAsync(this);

    [RelayCommand]
    private Task RemoveAsync() => owner.RemoveFromStackAsync(this);

    [RelayCommand]
    private void OpenPullRequest() => owner.OpenPullRequest(this);
}

/// <summary>
/// The Stacks tab: chains of branches, each built on the one before it. Stack a branch on top,
/// restack after changing a lower one, push the whole stack, and create the PRs (each into the branch below).
/// </summary>
public sealed partial class StacksViewModel : ObservableObject
{
    private readonly StackService _stacks;
    private readonly BranchService _branches;
    private readonly HistoryService _history;
    private readonly PullRequestService _pullRequestService;
    private readonly PullRequestDraftService _drafts;
    private readonly PullRequestsViewModel _pullRequests;
    private readonly BranchesViewModel _branchList;
    private readonly IDialogService _dialogs;
    private readonly IShellService _shell;

    private string? _repositoryPath;
    private string? _currentBranch;
    private string _trunk = "main";

    // A restack (or push) that stopped on a conflict picks up again once the rebase is finished.
    private string? _resumeStack;
    private bool _pushAfterResume;
    private bool _refreshing;
    private bool _resuming;
    private int _showVersion;

    public StacksViewModel(
        StackService stacks,
        BranchService branches,
        HistoryService history,
        PullRequestService pullRequestService,
        PullRequestDraftService drafts,
        PullRequestsViewModel pullRequests,
        BranchesViewModel branchList,
        IDialogService dialogs,
        IShellService shell)
    {
        _stacks = stacks;
        _branches = branches;
        _history = history;
        _pullRequestService = pullRequestService;
        _drafts = drafts;
        _pullRequests = pullRequests;
        _branchList = branchList;
        _dialogs = dialogs;
        _shell = shell;
    }

    public event Action<string>? MessageRaised;

    public event Action<string>? NoticeRaised;

    /// <summary>Raised after branches moved, so the window refreshes.</summary>
    public event Func<Task>? RepositoryChanged;

    /// <summary>Raised after pushing, so pull requests are asked for again.</summary>
    public event Func<Task>? Pushed;

    public ObservableCollection<StackItemViewModel> Stacks { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(SelectedTitle), nameof(SelectedSubtitle), nameof(SelectedNeedsRestack))]
    [NotifyCanExecuteChangedFor(nameof(RestackCommand), nameof(PushStackCommand), nameof(CreatePullRequestsCommand))]
    public partial StackItemViewModel? SelectedStack { get; set; }

    /// <summary>The selected stack's branches, top first (like they're drawn).</summary>
    public ObservableCollection<StackBranchViewModel> Branches { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RestackCommand), nameof(PushStackCommand), nameof(CreatePullRequestsCommand), nameof(StackOnTopCommand))]
    public partial bool IsBusy { get; set; }

    /// <summary>A rebase (or merge, ...) is stopped: stack actions wait for it.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RestackCommand), nameof(PushStackCommand), nameof(CreatePullRequestsCommand), nameof(StackOnTopCommand))]
    public partial bool IsOperationActive { get; set; }

    public bool HasSelection => SelectedStack is not null;

    /// <summary>Create PRs only makes sense on GitHub.</summary>
    public bool IsGitHubRepository => _pullRequests.IsGitHubRepository;

    public bool HasStacks => Stacks.Count > 0;

    public bool HasNoStacks => !HasStacks;

    public string SelectedTitle => SelectedStack?.Name ?? string.Empty;

    public string SelectedSubtitle => SelectedStack is null ? string.Empty : $"onto {SelectedStack.Stack.Trunk}";

    public bool SelectedNeedsRestack => SelectedStack?.Stack.NeedsRestack == true;

    /// <summary>Under the stack: what Restack would do, or that all is in place.</summary>
    public string Hint => SelectedStack?.Stack is not { } stack ? string.Empty
        : stack.NeedsRestack
            ? $"Restack replays the branches above {stack.Branches.First(branch => branch.NeedsRestack).Parent} onto its new version. A conflict stops it in the usual status bar; after Continue it picks up the rest."
            : "Every branch sits on the one below it. Push stack sends them all to GitHub.";

    /// <summary>Re-reads the stacks. Call on every window refresh (local git only, no network).</summary>
    public async Task RefreshAsync(string? repositoryPath, string? currentBranch, bool operationActive)
    {
        var repositoryChanged = repositoryPath != _repositoryPath;
        _repositoryPath = repositoryPath;
        _currentBranch = currentBranch;
        IsOperationActive = operationActive;

        if (repositoryChanged)
        {
            _resumeStack = null;
        }

        if (repositoryPath is null)
        {
            Stacks.Clear();
            Branches.Clear();
            SelectedStack = null;
            NotifyListChanged();
            return;
        }

        try
        {
            _trunk = await _stacks.GetTrunkAsync(repositoryPath);
            var stacks = await _stacks.GetStacksAsync(repositoryPath);
            var selectedName = SelectedStack?.Name;

            _refreshing = true;
            if (Stacks.Select(item => item.Name).SequenceEqual(stacks.Select(stack => stack.Name)))
            {
                // Same stacks: update them in place, so the list (and its selection) doesn't flicker.
                for (var index = 0; index < stacks.Count; index++)
                {
                    Stacks[index].Stack = stacks[index];
                }
            }
            else
            {
                Stacks.Clear();
                foreach (var stack in stacks)
                {
                    Stacks.Add(new StackItemViewModel(stack));
                }
            }

            // Keep the selection; else pick the stack you're on, else the first.
            SelectedStack = Stacks.FirstOrDefault(item => item.Name == selectedName)
                            ?? Stacks.FirstOrDefault(item => currentBranch is not null && item.Stack.Contains(currentBranch))
                            ?? Stacks.FirstOrDefault();
            _refreshing = false;
            OnPropertyChanged(nameof(SelectedTitle));
            OnPropertyChanged(nameof(SelectedSubtitle));
            await ShowBranchesAsync();
        }
        catch (GitCommandException exception)
        {
            MessageRaised?.Invoke(exception.Result.StandardError.Trim());
        }

        NotifyListChanged();
        OnPropertyChanged(nameof(IsGitHubRepository));

        // The rebase that stopped a restack is finished: carry on with the rest.
        if (!operationActive && _resumeStack is { } resume && Stacks.FirstOrDefault(item => item.Name == resume) is { } toResume)
        {
            var push = _pushAfterResume;
            _resumeStack = null;
            _pushAfterResume = false;
            SelectedStack = toResume;
            _resuming = true;
            try
            {
                await (push ? PushStackAsync() : RestackAsync());
            }
            finally
            {
                _resuming = false;
            }
        }
    }

    /// <summary>
    /// Called when a stopped rebase ends. After Abort the restack must not start again by itself;
    /// after Continue the next refresh carries on with the rest of the stack.
    /// </summary>
    public void OnOperationEnded(bool aborted)
    {
        if (aborted)
        {
            _resumeStack = null;
            _pushAfterResume = false;
        }
    }

    partial void OnSelectedStackChanged(StackItemViewModel? value)
    {
        if (!_refreshing)
        {
            _ = ShowBranchesAsync();
        }
    }

    /// <summary>A new branch on top of the checked-out one (starts a stack on the trunk or a plain branch).</summary>
    [RelayCommand(CanExecute = nameof(CanAct))]
    public async Task StackOnTopAsync()
    {
        if (_repositoryPath is not { } repository || _currentBranch is not { } current)
        {
            MessageRaised?.Invoke("Switch to a branch first: you're not on one.");
            return;
        }

        var answer = await _dialogs.AskNameAsync(new NameDialogOptions(
            $"Stack a branch on {current}",
            current == _trunk
                ? $"Starts a new stack on {_trunk}. Your uncommitted changes come along."
                : $"Builds on {current}: its pull request will go into {current}. Your uncommitted changes come along.",
            "feature/next-step",
            "Create and switch",
            name => _history.ValidateBranchNameAsync(repository, name)));

        if (answer is not null)
        {
            await RunAsync(async () =>
            {
                await _stacks.CreateOnTopAsync(repository, answer.Name, _trunk);
                NoticeRaised?.Invoke($"Created {answer.Name} on top of {current}");
            });
        }
    }

    [RelayCommand(CanExecute = nameof(CanActOnSelection))]
    private Task RestackAsync() => RestackSelectedAsync(pushAfter: false);

    /// <summary>Restacks if needed, then pushes every branch (new ones published, the rest force-pushed with lease).</summary>
    [RelayCommand(CanExecute = nameof(CanActOnSelection))]
    private async Task PushStackAsync()
    {
        if (SelectedStack?.Stack is not { } stack || _repositoryPath is not { } repository)
        {
            return;
        }

        if (stack.NeedsRestack && !await RestackSelectedAsync(pushAfter: true))
        {
            return; // stopped on a conflict (it pushes after Continue), or failed
        }

        stack = SelectedStack?.Stack ?? stack;
        await RunAsync(async () =>
        {
            var result = await _stacks.PushAsync(repository, stack);
            if (!result.Succeeded)
            {
                var error = result.StandardError.Trim().Split('\n').LastOrDefault(line => line.Trim().Length > 0) ?? "the push failed";
                MessageRaised?.Invoke(error.Contains("stale info", StringComparison.OrdinalIgnoreCase) || error.Contains("rejected", StringComparison.OrdinalIgnoreCase)
                    ? "GitHub has commits on one of these branches that you haven't fetched, so nothing was pushed. Fetch, check them, then push again."
                    : $"Nothing was pushed: {error}");
                return;
            }

            NoticeRaised?.Invoke($"Pushed {stack.Branches.Count} branches of {stack.Name}");
            if (Pushed is { } pushed)
            {
                await pushed();
            }
        });
    }

    /// <summary>
    /// Creates the missing pull requests (each into the branch below it) and adds the stack list to every
    /// PR's description. Pushes the stack first when GitHub doesn't have all of it.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanActOnSelection))]
    private async Task CreatePullRequestsAsync()
    {
        if (SelectedStack?.Stack is not { } stack || _repositoryPath is not { } repository)
        {
            return;
        }

        if (_pullRequests.Repository is not { } gitHub)
        {
            MessageRaised?.Invoke("This repository isn't on GitHub.");
            return;
        }

        if (!_pullRequests.IsSignedIn && !await _dialogs.ShowSignInAsync())
        {
            return;
        }

        var missing = stack.Branches.Where(branch => _pullRequests.ByBranch.GetValueOrDefault(branch.Name)?.IsActive != true).ToList();
        var plan = string.Join("\n", missing.Select(branch => $"• {branch.Name} into {branch.Parent}"));
        var (confirmed, asDrafts) = await _dialogs.ConfirmWithOptionAsync(
            missing.Count == 0 ? "Update the stack list in each pull request?" : $"Create {(missing.Count == 1 ? "1 pull request" : $"{missing.Count} pull requests")}?",
            missing.Count == 0
                ? "Every branch already has a pull request. This refreshes the stack list at the end of their descriptions."
                : $"{plan}\n\nThe stack is pushed first if GitHub doesn't have all of it. Each description gets the repo's PR template (or the commit list) and the stack list.",
            missing.Count == 0 ? "Update" : "Create",
            "Create them as drafts",
            optionDefault: false,
            dangerous: false);

        if (!confirmed)
        {
            return;
        }

        if (Branches.Any(branch => branch.NeedsPush) || stack.NeedsRestack)
        {
            await PushStackAsync();
            if (SelectedStack?.Stack is not { } pushedStack || pushedStack.NeedsRestack || Branches.Any(branch => branch.NeedsPush))
            {
                return; // the push said what went wrong
            }

            stack = pushedStack;
        }

        await RunAsync(async () =>
        {
            var numbers = new List<int>();
            var templates = await _drafts.FindTemplatesAsync(repository, $"origin/{_trunk}");

            foreach (var branch in stack.Branches)
            {
                if (_pullRequests.ByBranch.GetValueOrDefault(branch.Name) is { IsActive: true } existing)
                {
                    numbers.Add(existing.Number);
                    continue;
                }

                var subjects = await _drafts.GetCommitSubjectsAsync(repository, branch.Parent == _trunk ? $"origin/{_trunk}" : branch.Parent);
                var body = templates.FirstOrDefault()?.Content ?? PullRequestDraftService.CommitList(subjects);
                var created = await _pullRequestService.CreateAsync(
                    gitHub, branch.Name, branch.Parent, PullRequestDraftService.TitleFor(branch.Name, subjects), body, asDrafts);
                numbers.Add(created.Number);
            }

            // The stack list in every description, with this PR in bold.
            for (var index = 0; index < numbers.Count; index++)
            {
                var body = await _pullRequestService.GetBodyAsync(gitHub, numbers[index]);
                var updated = StackDescription.Apply(body, StackDescription.Section(stack.Trunk, numbers, index));
                if (updated != (body ?? string.Empty).ReplaceLineEndings("\n"))
                {
                    await _pullRequestService.UpdateBodyAsync(gitHub, numbers[index], updated);
                }
            }

            NoticeRaised?.Invoke(missing.Count == 0
                ? $"Updated the stack list in {numbers.Count} pull requests"
                : $"Created {(missing.Count == 1 ? "1 pull request" : $"{missing.Count} pull requests")} for {stack.Name}");

            if (Pushed is { } pushed)
            {
                await pushed(); // asks GitHub again, so the badges show the new PRs
            }
        });
    }

    public Task SwitchToAsync(StackBranchViewModel branch) => _branchList.SwitchToAsync(branch.Name);

    public async Task RemoveFromStackAsync(StackBranchViewModel branch)
    {
        if (_repositoryPath is not { } repository)
        {
            return;
        }

        if (await _dialogs.ConfirmAsync(
                $"Take {branch.Name} out of its stack?",
                "The branch and its commits stay. My Git just stops treating it as part of the stack; the branch above it (if any) then builds on the one below.",
                "Take out"))
        {
            await RunAsync(() => _stacks.RemoveAsync(repository, branch.Name));
        }
    }

    public void OpenPullRequest(StackBranchViewModel branch)
    {
        if (branch.PullRequest is { } pullRequest)
        {
            _shell.OpenUrl(pullRequest.Url);
        }
    }

    private bool CanAct() => !IsBusy && !IsOperationActive;

    private bool CanActOnSelection() => CanAct() && SelectedStack is not null;

    // Returns true when the stack is in place afterwards.
    private async Task<bool> RestackSelectedAsync(bool pushAfter)
    {
        if (SelectedStack?.Stack is not { } stack || _repositoryPath is not { } repository)
        {
            return false;
        }

        var outcome = RebaseOutcome.UpToDate;
        await RunAsync(async () =>
        {
            outcome = await _stacks.RestackAsync(repository, stack, _currentBranch, autoStash: true);
            switch (outcome)
            {
                case RebaseOutcome.Done:
                    NoticeRaised?.Invoke($"Restacked {stack.Name}");
                    break;
                case RebaseOutcome.UpToDate when !_resuming: // after Continue, "Rebase finished" already says it
                    NoticeRaised?.Invoke($"{stack.Name} is already in place");
                    break;
                case RebaseOutcome.Conflict:
                case RebaseOutcome.Stopped:
                    _resumeStack = stack.Name;
                    _pushAfterResume = pushAfter;
                    MessageRaised?.Invoke(pushAfter
                        ? "Restacking stopped on a conflict. Resolve it under Conflicts and press Continue: the rest of the stack follows, then it's pushed."
                        : "Restacking stopped on a conflict. Resolve it under Conflicts and press Continue: the rest of the stack follows.");
                    break;
            }
        });

        return outcome is RebaseOutcome.Done or RebaseOutcome.UpToDate;
    }

    // Refreshes can overlap; only the latest one fills in the cards.
    private async Task ShowBranchesAsync()
    {
        var version = ++_showVersion;
        var cards = new List<StackBranchViewModel>();

        if (SelectedStack?.Stack is { } stack && _repositoryPath is { } repository)
        {
            var local = (await _branches.ListAsync(repository)).Local.ToDictionary(branch => branch.Name);
            cards.AddRange(stack.Branches.Reverse().Select(entry => new StackBranchViewModel(
                entry, local.GetValueOrDefault(entry.Name), _pullRequests.ByBranch.GetValueOrDefault(entry.Name), entry.Name == _currentBranch, this)));
        }

        if (version != _showVersion)
        {
            return;
        }

        Branches.Clear();
        foreach (var card in cards)
        {
            Branches.Add(card);
        }

        OnPropertyChanged(nameof(Hint));
        OnPropertyChanged(nameof(SelectedNeedsRestack));
    }

    private void NotifyListChanged()
    {
        OnPropertyChanged(nameof(HasStacks));
        OnPropertyChanged(nameof(HasNoStacks));
        OnPropertyChanged(nameof(Hint));
    }

    private async Task RunAsync(Func<Task> action)
    {
        IsBusy = true;
        try
        {
            await action();
        }
        catch (Exception exception) when (exception is GitCommandException or InvalidOperationException or PullRequestException or Octokit.ApiException or HttpRequestException)
        {
            MessageRaised?.Invoke(exception is GitCommandException git && git.Result.StandardError.Trim() is { Length: > 0 } error
                ? error.Split('\n')[^1]
                : exception.Message);
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
