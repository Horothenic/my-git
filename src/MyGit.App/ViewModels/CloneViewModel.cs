using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyGit.App.Services;
using MyGit.Core.Clone;
using MyGit.Core.Git;
using MyGit.Core.Repositories;
using MyGit.Core.Settings;
using MyGit.GitHub.Accounts;
using MyGit.GitHub.Repositories;
using Octokit;

namespace MyGit.App.ViewModels;

/// <summary>
/// The clone dialog: pick one of your GitHub repositories (or paste a URL), pick a folder, clone.
/// </summary>
public sealed partial class CloneViewModel : ObservableObject
{
    private const int RepositoriesTab = 0;
    private const int UrlTab = 1;

    private readonly CloneService _cloneService;
    private readonly GitHubRepositoryService _gitHubRepositories;
    private readonly AccountManager _accounts;
    private readonly RepositoryStore _repositoryStore;
    private readonly RepositoryService _repositoryService;
    private readonly AppSettingsStore _settingsStore;
    private readonly IFolderPicker _folderPicker;

    private List<CloneRepositoryItemViewModel> _allItems = [];
    private CancellationTokenSource? _cloneCancellation;

    public CloneViewModel(
        CloneService cloneService,
        GitHubRepositoryService gitHubRepositories,
        AccountManager accounts,
        RepositoryStore repositoryStore,
        RepositoryService repositoryService,
        AppSettingsStore settingsStore,
        IFolderPicker folderPicker)
    {
        _cloneService = cloneService;
        _gitHubRepositories = gitHubRepositories;
        _accounts = accounts;
        _repositoryStore = repositoryStore;
        _repositoryService = repositoryService;
        _settingsStore = settingsStore;
        _folderPicker = folderPicker;

        DestinationFolder = settingsStore.Settings.LastCloneFolder ?? DefaultCloneFolder;

        // Signed out: start on the URL tab, the list needs an account.
        SelectedTab = IsSignedIn ? RepositoriesTab : UrlTab;
    }

    /// <summary>Raised when the dialog should close, with the cloned path (null if cancelled).</summary>
    public event Action<string?>? Finished;

    /// <summary>Raised when the user asks to sign in from the dialog.</summary>
    public event Action? SignInRequested;

    public static string DefaultCloneFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Repositories");

    public bool IsSignedIn => _accounts.HasToken;

    public ObservableCollection<CloneRepositoryGroupViewModel> Groups { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TargetPath), nameof(TargetPathText), nameof(CanClone))]
    public partial int SelectedTab { get; set; }

    [ObservableProperty]
    public partial bool IsLoadingRepositories { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLoadError))]
    public partial string? LoadError { get; set; }

    public bool HasLoadError => LoadError is not null;

    [ObservableProperty]
    public partial string FilterText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TargetPath), nameof(TargetPathText), nameof(CanClone))]
    public partial CloneRepositoryItemViewModel? SelectedRepository { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TargetPath), nameof(TargetPathText), nameof(CanClone))]
    public partial string Url { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TargetPath), nameof(TargetPathText), nameof(CanClone))]
    public partial string DestinationFolder { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsForm), nameof(IsCloning), nameof(IsStopping), nameof(IsFailed), nameof(IsBusy), nameof(CanClone), nameof(WindowTitle))]
    public partial CloneMode Mode { get; set; } = CloneMode.Form;

    public bool IsForm => Mode == CloneMode.Form;

    public bool IsCloning => Mode == CloneMode.Cloning;

    public bool IsStopping => Mode == CloneMode.Stopping;

    public bool IsFailed => Mode == CloneMode.Failed;

    /// <summary>True while git runs (cloning or stopping): the window can't be dismissed.</summary>
    public bool IsBusy => Mode is CloneMode.Cloning or CloneMode.Stopping;

    public string WindowTitle => Mode == CloneMode.Form ? "Clone a repository" : $"Cloning {CloningName}";

    /// <summary>The checklist shown while cloning.</summary>
    public ObservableCollection<CloneStageViewModel> Stages { get; } = [];

    /// <summary>Repository name shown in the cloning popup, e.g. "game-client".</summary>
    [ObservableProperty]
    public partial string CloningName { get; set; } = string.Empty;

    /// <summary>Owner/name and visibility, or the URL when cloning from the URL tab.</summary>
    [ObservableProperty]
    public partial string CloningSource { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string CloningTarget { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ElapsedText { get; set; } = "0:00";

    [ObservableProperty]
    public partial string? CloneError { get; set; }

    /// <summary>The URL that will be cloned, from the selected repository or the URL tab.</summary>
    public string? SourceUrl => SelectedTab == RepositoriesTab
        ? SelectedRepository?.Repository.CloneUrl
        : string.IsNullOrWhiteSpace(Url) ? null : Url.Trim();

    /// <summary>The folder that will be created.</summary>
    public string? TargetPath => SourceUrl is null || string.IsNullOrWhiteSpace(DestinationFolder)
        ? null
        : Path.Combine(DestinationFolder.Trim(), CloneService.FolderNameFromUrl(SourceUrl));

    public string TargetPathText
    {
        get
        {
            if (TargetPath is null)
            {
                return SelectedTab == RepositoriesTab ? "Pick a repository above." : "Paste a repository URL above.";
            }

            return CloneService.ValidateTarget(TargetPath) is { } problem
                ? $"{TargetPath}: {problem}"
                : $"Creates {TargetPath}";
        }
    }

    public bool CanClone => IsForm && TargetPath is not null && CloneService.ValidateTarget(TargetPath) is null;

    /// <summary>Call when the dialog opens.</summary>
    public async Task LoadAsync()
    {
        if (!IsSignedIn)
        {
            return;
        }

        IsLoadingRepositories = true;
        LoadError = null;

        try
        {
            var clonedRemotes = await GetClonedRemotesAsync();
            var repositories = await _gitHubRepositories.ListAsync();

            _allItems = repositories
                .Select(repository => new CloneRepositoryItemViewModel(
                    repository,
                    clonedRemotes.Contains(RepositoryService.NormalizeRemoteUrl(repository.CloneUrl)),
                    Select))
                .ToList();

            ApplyFilter();
        }
        catch (Exception exception) when (exception is ApiException or HttpRequestException)
        {
            LoadError = "Couldn't load your repositories from GitHub. You can still clone by URL.";
        }
        finally
        {
            IsLoadingRepositories = false;
        }
    }

    /// <summary>Call after the user signed in from inside the dialog.</summary>
    public async Task OnSignedInAsync()
    {
        OnPropertyChanged(nameof(IsSignedIn));
        SelectedTab = RepositoriesTab;
        await LoadAsync();
    }

    [RelayCommand]
    private void SignIn() => SignInRequested?.Invoke();

    [RelayCommand]
    private async Task BrowseAsync()
    {
        var folder = await _folderPicker.PickFolderAsync("Clone into folder");

        if (folder is not null)
        {
            DestinationFolder = folder;
        }
    }

    [RelayCommand]
    private async Task CloneAsync()
    {
        if (!CanClone || SourceUrl is not { } url || TargetPath is not { } target)
        {
            return;
        }

        CloningName = CloneService.FolderNameFromUrl(url);
        CloningSource = SelectedTab == RepositoriesTab && SelectedRepository is { } picked
            ? $"{picked.Repository.Owner}/{picked.Name}  ·  {(picked.IsPrivate ? "private" : "public")}"
            : url;
        CloningTarget = target;

        var tracker = new CloneStageTracker();
        Stages.Clear();
        foreach (var stage in tracker.Stages)
        {
            Stages.Add(new CloneStageViewModel(stage.DisplayName));
        }

        CloneError = null;
        Mode = CloneMode.Cloning;
        _cloneCancellation = new CancellationTokenSource();
        var elapsed = TrackElapsedAsync(_cloneCancellation.Token);

        // Progress<T> raises its callback on the UI thread, where bindings must be updated.
        var progress = new Progress<CloneProgress>(update =>
        {
            if (tracker.Apply(update))
            {
                UpdateStages(tracker);
            }
        });

        try
        {
            await _cloneService.CloneAsync(url, target, progress, _cloneCancellation.Token);

            tracker.CompleteAll();
            UpdateStages(tracker);

            _settingsStore.Settings.LastCloneFolder = DestinationFolder.Trim();
            _settingsStore.Save();
            _repositoryStore.Add(RepositoryInfo.FromPath(target));

            Finished?.Invoke(target);
        }
        catch (OperationCanceledException)
        {
            // Cancel pressed: CloneService already removed the partial folder. Back to the form.
            Mode = CloneMode.Form;
        }
        catch (Exception exception) when (exception is GitCommandException or InvalidOperationException or IOException)
        {
            CloneError = exception is GitCommandException git
                ? DescribeCloneError(git.Result.StandardError)
                : exception.Message;
            Mode = CloneMode.Failed;
        }
        finally
        {
            _cloneCancellation.Cancel(); // stops the elapsed timer
            await elapsed;
            OnPropertyChanged(nameof(TargetPathText));
            OnPropertyChanged(nameof(CanClone));
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        if (Mode == CloneMode.Cloning)
        {
            Mode = CloneMode.Stopping;
            _cloneCancellation?.Cancel();
            return;
        }

        if (Mode == CloneMode.Form)
        {
            Finished?.Invoke(null);
        }
    }

    /// <summary>From the failed screen: back to the form with every choice kept.</summary>
    [RelayCommand]
    private void Back() => Mode = CloneMode.Form;

    /// <summary>From the failed screen: run the same clone again.</summary>
    [RelayCommand]
    private Task TryAgainAsync()
    {
        Mode = CloneMode.Form;
        return CloneAsync();
    }

    /// <summary>Stops a running clone without asking the dialog to close (it's already closing).</summary>
    public void StopCloning() => _cloneCancellation?.Cancel();

    private void UpdateStages(CloneStageTracker tracker)
    {
        for (var i = 0; i < tracker.Stages.Count; i++)
        {
            Stages[i].Update(tracker.Stages[i]);
        }
    }

    private async Task TrackElapsedAsync(CancellationToken cancellationToken)
    {
        var started = DateTime.Now;
        ElapsedText = "0:00";

        try
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                var elapsed = DateTime.Now - started;
                ElapsedText = elapsed.TotalHours >= 1 ? elapsed.ToString(@"h\:mm\:ss") : elapsed.ToString(@"m\:ss");
            }
        }
        catch (OperationCanceledException)
        {
            // Clone finished, failed or was cancelled.
        }
    }

    partial void OnFilterTextChanged(string value) => ApplyFilter();

    private void Select(CloneRepositoryItemViewModel item)
    {
        if (SelectedRepository is not null)
        {
            SelectedRepository.IsSelected = false;
        }

        item.IsSelected = true;
        SelectedRepository = item;
    }

    private void ApplyFilter()
    {
        Groups.Clear();

        var visible = _allItems.Where(item =>
            FilterText.Length == 0 ||
            item.Name.Contains(FilterText, StringComparison.OrdinalIgnoreCase) ||
            item.Repository.Owner.Contains(FilterText, StringComparison.OrdinalIgnoreCase));

        // Your own repositories first, then each organization alphabetically.
        var login = _accounts.Account?.Login;
        var groups = visible
            .GroupBy(item => item.Repository.Owner)
            .OrderBy(group => string.Equals(group.Key, login, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            Groups.Add(new CloneRepositoryGroupViewModel(group.Key, group.ToList()));
        }
    }

    private async Task<HashSet<string>> GetClonedRemotesAsync()
    {
        var remotes = new HashSet<string>();

        foreach (var repository in _repositoryStore.Repositories.Where(r => Directory.Exists(r.Path)))
        {
            if (await _repositoryService.GetRemoteUrlAsync(repository.Path) is { } remote)
            {
                remotes.Add(RepositoryService.NormalizeRemoteUrl(remote));
            }
        }

        return remotes;
    }

    private static string DescribeCloneError(string gitError)
    {
        if (gitError.Contains("could not read Username", StringComparison.OrdinalIgnoreCase) ||
            gitError.Contains("Authentication failed", StringComparison.OrdinalIgnoreCase))
        {
            return "GitHub needs you to sign in to clone this repository.";
        }

        if (gitError.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            return "That repository doesn't exist, or your account can't access it.";
        }

        var lastLine = gitError.Split('\n', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim();
        return string.IsNullOrEmpty(lastLine) ? "The clone failed." : lastLine;
    }
}
