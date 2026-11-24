using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyGit.App.Services;
using MyGit.Core.PullRequests;
using MyGit.Core.Repositories;
using MyGit.GitHub.PullRequests;

namespace MyGit.App.ViewModels.PullRequests;

/// <summary>
/// The pull request button in the toolbar: creates a pull request (pushes first, then opens the dialog),
/// or opens the branch's pull request on GitHub (the icon is then colored by its state).
/// GitHub is asked on open, branch switch, fetch, push and after creating; never in the background.
/// </summary>
public sealed partial class PullRequestsViewModel : ObservableObject
{
    private readonly PullRequestService _pullRequests;
    private readonly PullRequestDraftService _drafts;
    private readonly RepositoryService _repositories;
    private readonly IDialogService _dialogs;
    private readonly IShellService _shell;

    private string? _repositoryPath;
    private GitHubRepositoryId? _repository;
    private RepositoryBranches? _branches;
    private IReadOnlyDictionary<string, PullRequestInfo> _byBranch = new Dictionary<string, PullRequestInfo>();

    public PullRequestsViewModel(PullRequestService pullRequests, PullRequestDraftService drafts, RepositoryService repositories, IDialogService dialogs, IShellService shell)
    {
        _pullRequests = pullRequests;
        _drafts = drafts;
        _repositories = repositories;
        _dialogs = dialogs;
        _shell = shell;
    }

    /// <summary>Pushes (or publishes) the current branch if needed; true when GitHub has all of it. Set by the window (the Push button's logic).</summary>
    public Func<Task<bool>>? EnsurePushed { get; set; }

    /// <summary>Raised with every branch's pull request after GitHub was asked (for the badges in the Branch dropdown).</summary>
    public event Action<IReadOnlyDictionary<string, PullRequestInfo>>? PullRequestsChanged;

    /// <summary>Raised with a short success note.</summary>
    public event Action<string>? NoticeRaised;

    /// <summary>Raised with an error for the status bar.</summary>
    public event Action<string>? MessageRaised;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAvailable))]
    public partial string? Branch { get; set; }

    /// <summary>The current branch's pull request, if it has one.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPullRequest), nameof(ToolTip), nameof(IsOpen), nameof(IsDraft), nameof(IsMerged), nameof(IsClosed))]
    [NotifyCanExecuteChangedFor(nameof(OpenCommand), nameof(CreateCommand))]
    public partial PullRequestInfo? Current { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAvailable))]
    public partial bool IsGitHubRepository { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(MainCommand), nameof(OpenCommand), nameof(CreateCommand))]
    public partial bool IsBusy { get; set; }

    /// <summary>Shown for a GitHub repository on a branch other than the default one.</summary>
    public bool IsAvailable => IsGitHubRepository && Branch is not null && Branch != _branches?.DefaultBranch;

    public bool HasPullRequest => Current is not null;

    public bool IsOpen => Current?.State == PullRequestState.Open;

    public bool IsDraft => Current?.State == PullRequestState.Draft;

    public bool IsMerged => Current?.State == PullRequestState.Merged;

    public bool IsClosed => Current?.State == PullRequestState.Closed;

    public string ToolTip => Current is { } pullRequest
        ? $"Pull request #{pullRequest.Number} ({pullRequest.State.ToString().ToLowerInvariant()}): {pullRequest.Title}. Click to open it on GitHub."
        : !_pullRequests.IsSignedIn
            ? "Sign in to GitHub to create pull requests"
            : $"Create a pull request for {Branch} (pushes it first if needed)";

    /// <summary>
    /// Call on every window refresh: cheap unless <paramref name="askGitHub"/> is set or the repository changed,
    /// since only then GitHub is asked again.
    /// </summary>
    public async Task RefreshAsync(string? repositoryPath, string? branch, bool askGitHub = false)
    {
        if (repositoryPath != _repositoryPath)
        {
            _repositoryPath = repositoryPath;
            _repository = null;
            _branches = null;
            _byBranch = new Dictionary<string, PullRequestInfo>();
            askGitHub = true;

            if (repositoryPath is not null)
            {
                var remote = await _repositories.GetRemoteUrlAsync(repositoryPath);
                _repository = GitHubRepositoryId.FromNormalizedRemote(remote is null ? null : RepositoryService.NormalizeRemoteUrl(remote));
            }

            IsGitHubRepository = _repository is not null;
        }

        Branch = branch;

        if (askGitHub && _repository is { } repository && _pullRequests.IsSignedIn)
        {
            try
            {
                _branches = await _pullRequests.GetBranchesAsync(repository);
                _byBranch = PullRequestService.ByBranch(await _pullRequests.ListAsync(repository));
                PullRequestsChanged?.Invoke(_byBranch);
            }
            catch (Exception exception) when (exception is Octokit.ApiException or HttpRequestException)
            {
                // Offline, or no access: the button just offers to create; creating will say what's wrong.
            }
        }

        Current = branch is not null ? _byBranch.GetValueOrDefault(branch) : null;
        OnPropertyChanged(nameof(IsAvailable));
        OnPropertyChanged(nameof(ToolTip));
    }

    /// <summary>Signing in or out changes what GitHub tells us.</summary>
    public Task OnAccountChangedAsync()
    {
        var path = _repositoryPath;
        _repositoryPath = null; // forces a full reload
        return RefreshAsync(path, Branch);
    }

    /// <summary>The button: open the PR if there is one, else create one.</summary>
    [RelayCommand(CanExecute = nameof(IsNotBusy))]
    private Task MainAsync() => Current is not null ? OpenAsync() : CreateAsync();

    [RelayCommand(CanExecute = nameof(HasPullRequestAndNotBusy))]
    private Task OpenAsync()
    {
        if (Current is { } pullRequest)
        {
            _shell.OpenUrl(pullRequest.Url);
        }

        return Task.CompletedTask;
    }

    /// <summary>Also offered for a branch whose PR was merged or closed (right-click).</summary>
    [RelayCommand(CanExecute = nameof(CanCreate))]
    private async Task CreateAsync()
    {
        if (_repositoryPath is not { } repositoryPath || _repository is not { } repository || Branch is not { } head)
        {
            return;
        }

        if (!_pullRequests.IsSignedIn && !await _dialogs.ShowSignInAsync())
        {
            return;
        }

        IsBusy = true;
        try
        {
            if (EnsurePushed is { } ensurePushed && !await ensurePushed())
            {
                return; // the push explained what went wrong
            }

            _branches ??= await _pullRequests.GetBranchesAsync(repository);
            var bases = _branches.Branches.Where(name => name != head).ToList();
            if (bases.Count == 0)
            {
                MessageRaised?.Invoke("GitHub has no other branch to merge this one into.");
                return;
            }

            var dialog = new CreatePullRequestViewModel(
                head,
                bases,
                bases.Contains(_branches.DefaultBranch) ? _branches.DefaultBranch : bases[0],
                loadDraft: async @base => (
                    await _drafts.GetCommitSubjectsAsync(repositoryPath, $"origin/{@base}"),
                    await _drafts.FindTemplatesAsync(repositoryPath, $"origin/{@base}")),
                create: (@base, title, body, draft) => _pullRequests.CreateAsync(repository, head, @base, title, body, draft));

            await dialog.LoadAsync();
            if (!await _dialogs.ShowCreatePullRequestAsync(dialog) || dialog.Created is not { } created)
            {
                return;
            }

            NoticeRaised?.Invoke($"Created pull request #{created.Number}. The pull request button opens it on GitHub.");
            await RefreshAsync(repositoryPath, head, askGitHub: true);
            Current ??= created; // GitHub's list can lag a moment behind
        }
        catch (Exception exception) when (exception is Octokit.ApiException or HttpRequestException or InvalidOperationException)
        {
            MessageRaised?.Invoke($"Couldn't reach GitHub: {exception.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool IsNotBusy() => !IsBusy;

    private bool HasPullRequestAndNotBusy() => Current is not null && !IsBusy;

    private bool CanCreate() => !IsBusy && Current?.IsActive != true;
}
