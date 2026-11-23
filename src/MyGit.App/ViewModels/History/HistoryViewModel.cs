using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyGit.App.Services;
using MyGit.App.ViewModels.Changes;
using MyGit.Core.Changes;
using MyGit.Core.Diffs;
using MyGit.Core.Git;
using MyGit.Core.History;

namespace MyGit.App.ViewModels.History;

/// <summary>
/// The History tab: the commit graph, the selected commit's details and files, and a read-only diff.
/// </summary>
public sealed partial class HistoryViewModel : ObservableObject
{
    private const int PageSize = 300;

    private readonly HistoryService _history;
    private readonly IGitRunner _git;
    private readonly IDialogService _dialogs;
    private readonly IShellService _shell;

    private GraphLayout _layout = new();
    private string? _repositoryPath;
    private string? _refsFingerprint;
    private bool _hasMore;
    private bool _loadingMore;
    private CancellationTokenSource? _detailsLoading;

    public HistoryViewModel(HistoryService history, IGitRunner git, DiffService diffs, IDialogService dialogs, IShellService shell)
    {
        _history = history;
        _git = git;
        _dialogs = dialogs;
        _shell = shell;
        Diff = new DiffViewModel(diffs) { EmptyText = "Select a file to see what this commit changed in it." };
    }

    /// <summary>Raised with an error for the message bar.</summary>
    public event Action<string>? MessageRaised;

    /// <summary>Raised after a branch or tag was created or HEAD moved, so the window refreshes.</summary>
    public event Func<Task>? RepositoryChanged;

    public ObservableCollection<CommitRowViewModel> Commits { get; } = [];

    /// <summary>One width for every row's graph (the widest so far), so messages line up.</summary>
    [ObservableProperty]
    public partial double GraphWidth { get; set; } = Controls.GraphCell.LaneWidth + 4;

    public DiffViewModel Diff { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    public partial CommitRowViewModel? SelectedCommit { get; set; }

    public bool HasSelection => SelectedCommit is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DetailsMeta), nameof(DetailsMessage), nameof(DetailsShortSha))]
    public partial CommitDetails? Details { get; set; }

    public string DetailsMessage => Details?.Message ?? string.Empty;

    public string DetailsShortSha => Details?.Sha[..7] ?? string.Empty;

    public string DetailsMeta => Details is null
        ? string.Empty
        : $"{Details.Author} <{Details.AuthorEmail}> · {Details.AuthoredAt.ToString("f", CultureInfo.CurrentCulture)}" +
          (Details.Parents.Count > 1 ? $" · merge of {Details.Parents.Count} parents" : string.Empty);

    public ObservableCollection<FileChange> Files { get; } = [];

    [ObservableProperty]
    public partial FileChange? SelectedFile { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial bool IsLoaded { get; set; }

    public bool IsEmpty => IsLoaded && Commits.Count == 0;

    /// <summary>
    /// Loads (or reloads) the history. Cheap to call often: it only reloads when HEAD,
    /// a branch or a tag moved. The selected commit stays selected if it's still there.
    /// </summary>
    public async Task RefreshAsync(string? repositoryPath)
    {
        if (repositoryPath != _repositoryPath)
        {
            _repositoryPath = repositoryPath;
            _refsFingerprint = null;
            Commits.Clear();
            SelectedCommit = null;
            IsLoaded = false;
        }

        if (repositoryPath is null)
        {
            return;
        }

        var fingerprint = await GetRefsFingerprintAsync(repositoryPath);
        if (fingerprint == _refsFingerprint)
        {
            return;
        }

        _refsFingerprint = fingerprint;
        var selectedSha = SelectedCommit?.Sha;

        try
        {
            _layout = new GraphLayout();
            var page = await LoadPageAsync(repositoryPath, skip: 0);

            Commits.Clear();
            foreach (var row in page)
            {
                Commits.Add(row);
            }

            IsLoaded = true;
            OnPropertyChanged(nameof(IsEmpty));
            SelectedCommit = Commits.FirstOrDefault(commit => commit.Sha == selectedSha);
        }
        catch (GitCommandException exception)
        {
            MessageRaised?.Invoke(exception.Message);
        }
    }

    /// <summary>Called as the list scrolls near its end.</summary>
    public async Task LoadMoreAsync()
    {
        if (!_hasMore || _loadingMore || _repositoryPath is not { } repository)
        {
            return;
        }

        _loadingMore = true;

        try
        {
            foreach (var row in await LoadPageAsync(repository, skip: Commits.Count))
            {
                Commits.Add(row);
            }
        }
        catch (GitCommandException exception)
        {
            MessageRaised?.Invoke(exception.Message);
        }
        finally
        {
            _loadingMore = false;
        }
    }

    partial void OnSelectedCommitChanged(CommitRowViewModel? value) => _ = LoadDetailsAsync(value);

    partial void OnSelectedFileChanged(FileChange? value)
    {
        if (_repositoryPath is { } repository && Details is { } details)
        {
            _ = Diff.ShowCommitFileAsync(repository, value, token => _history.GetFileDiffAsync(repository, details, value!, token));
        }
    }

    // ---- Right-click actions (the view passes the clicked row) ----

    public async Task CreateBranchAsync(CommitRowViewModel commit, bool fromTag)
    {
        if (_repositoryPath is not { } repository)
        {
            return;
        }

        var startPoint = fromTag && commit.FirstTag is { } tag ? tag : commit.Sha;
        var from = fromTag && commit.FirstTag is { } tagName ? $"From tag {tagName}" : $"From {commit.ShortSha} {commit.Subject}";

        var answer = await _dialogs.AskNameAsync(new NameDialogOptions(
            "Create branch",
            from,
            "feature/my-change",
            "Create branch",
            name => _history.ValidateBranchNameAsync(repository, name),
            CheckBoxText: "Switch to the new branch",
            CheckBoxDefault: true));

        if (answer is not null)
        {
            await RunAsync(() => _history.CreateBranchAsync(repository, answer.Name, startPoint, answer.IsChecked));
        }
    }

    public async Task CreateTagAsync(CommitRowViewModel commit)
    {
        if (_repositoryPath is not { } repository)
        {
            return;
        }

        var answer = await _dialogs.AskNameAsync(new NameDialogOptions(
            "Create tag",
            $"On {commit.ShortSha} {commit.Subject}",
            "v1.2.0",
            "Create tag",
            name => _history.ValidateTagNameAsync(repository, name),
            CheckBoxText: "Push the tag to GitHub",
            MessagePlaceholder: "Message (optional; makes it an annotated tag)"));

        if (answer is not null)
        {
            await RunAsync(() => _history.CreateTagAsync(repository, answer.Name, commit.Sha, answer.Message, answer.IsChecked));
        }
    }

    public async Task CheckoutAsync(CommitRowViewModel commit)
    {
        if (_repositoryPath is not { } repository)
        {
            return;
        }

        var ok = await _dialogs.ConfirmAsync(
            $"Check out {commit.ShortSha}?",
            "You'll be on that commit without a branch (\"detached HEAD\"), handy for looking at an old version. " +
            "Commits you make there aren't on any branch until you create one. Switch back to a branch any time.",
            "Check out");

        if (ok)
        {
            await RunAsync(() => _history.CheckoutDetachedAsync(repository, commit.Sha));
        }
    }

    public Task CopyShaAsync(CommitRowViewModel commit) => _shell.CopyTextAsync(commit.Sha);

    public Task CopyMessageAsync(CommitRowViewModel commit) => _shell.CopyTextAsync(commit.Subject);

    [RelayCommand]
    private Task CopyDetailsShaAsync() => Details is { } details ? _shell.CopyTextAsync(details.Sha) : Task.CompletedTask;

    private async Task<List<CommitRowViewModel>> LoadPageAsync(string repository, int skip)
    {
        var commits = await _history.GetCommitsAsync(repository, skip, PageSize);
        commits = await _history.ClassifyRefsAsync(repository, commits);
        _hasMore = commits.Count == PageSize;

        var rows = commits.Select(commit => new CommitRowViewModel(commit, _layout.Add(commit.Sha, commit.Parents))).ToList();

        var widest = rows.Count == 0 ? 1 : rows.Max(row => row.Graph.LaneCount);
        GraphWidth = Math.Max(skip == 0 ? 0 : GraphWidth, widest * Controls.GraphCell.LaneWidth + 4);

        return rows;
    }

    private async Task LoadDetailsAsync(CommitRowViewModel? commit)
    {
        _detailsLoading?.Cancel();
        _detailsLoading = new CancellationTokenSource();
        var token = _detailsLoading.Token;

        Files.Clear();
        SelectedFile = null;
        await Diff.ShowCommitFileAsync(_repositoryPath ?? string.Empty, null, _ => Task.FromResult(FileDiff.Binary(string.Empty)));

        if (commit is null || _repositoryPath is not { } repository)
        {
            Details = null;
            return;
        }

        try
        {
            var details = await _history.GetDetailsAsync(repository, commit.Sha, token);
            if (token.IsCancellationRequested)
            {
                return;
            }

            Details = details;
            foreach (var file in details.Files)
            {
                Files.Add(file);
            }

            // Show the first file right away, so the diff isn't empty.
            SelectedFile = Files.FirstOrDefault();
        }
        catch (OperationCanceledException)
        {
            // Another commit was selected.
        }
        catch (GitCommandException exception)
        {
            MessageRaised?.Invoke(exception.Message);
        }
    }

    /// <summary>Something that changes only when a branch, tag or HEAD moves.</summary>
    private async Task<string> GetRefsFingerprintAsync(string repository)
    {
        var head = await _git.RunAsync(repository, ["rev-parse", "HEAD"]);
        var refs = await _git.RunAsync(repository, ["for-each-ref", "--format=%(objectname) %(refname)"]);
        return head.StandardOutput + refs.StandardOutput;
    }

    private async Task RunAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (GitCommandException exception)
        {
            var error = exception.Result.StandardError.Trim();
            MessageRaised?.Invoke(error.Length > 0 ? error : exception.Message);
        }

        if (RepositoryChanged is { } handler)
        {
            await handler();
        }
    }
}
