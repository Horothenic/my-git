using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyGit.App.Services;
using MyGit.App.ViewModels.Changes;
using MyGit.Core.Git;
using MyGit.Core.Repositories;
using MyGit.Core.Settings;

namespace MyGit.App.ViewModels;

/// <summary>
/// The main window: toolbar state, the open repository and messages for the user.
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly RepositoryService _repositoryService;
    private readonly AppSettingsStore _settingsStore;
    private readonly ThemeService _themeService;
    private readonly IDialogService _dialogs;

    // Switch to the Changes tab once per stop, so you can still look elsewhere while resolving.
    private bool _conflictsShown;

    public MainWindowViewModel(
        RepositoryStore repositoryStore,
        RepositoryService repositoryService,
        AppSettingsStore settingsStore,
        ThemeService themeService,
        IFolderPicker folderPicker,
        IDialogService dialogs,
        AccountViewModel account,
        ChangesViewModel changes,
        SyncViewModel sync,
        History.HistoryViewModel history,
        BranchesViewModel branches,
        UndoViewModel undo,
        OperationViewModel operation,
        PullRequests.PullRequestsViewModel pullRequests,
        Func<PullRequests.PullRequestsViewModel, BranchesViewModel, Stacks.StacksViewModel> createStacks)
    {
        _repositoryService = repositoryService;
        _settingsStore = settingsStore;
        _themeService = themeService;
        _dialogs = dialogs;
        Account = account;
        Changes = changes;
        Sync = sync;
        History = history;
        Branches = branches;
        Undo = undo;
        Operation = operation;
        PullRequests = pullRequests;
        PullRequests.EnsurePushed = Sync.EnsurePushedAsync;
        PullRequests.PullRequestsChanged += Branches.ApplyPullRequests;
        PullRequests.NoticeRaised += Sync.ShowNotice;
        PullRequests.MessageRaised += ShowMessage;
        Stacks = createStacks(pullRequests, branches);
        Stacks.MessageRaised += ShowMessage;
        Stacks.NoticeRaised += Sync.ShowNotice;
        Stacks.RepositoryChanged += RefreshAsync;
        Stacks.Pushed += () => PullRequests.RefreshAsync(CurrentRepository?.Path, PullRequests.Branch, askGitHub: true);
        Branches.StackOnTopRequested += () => Stacks.StackOnTopAsync();
        Operation.Ended += Stacks.OnOperationEnded;
        Operation.Ended += _ => Message = null; // "stopped on a conflict…" no longer applies
        Sync.RemoteUpdated += () => PullRequests.RefreshAsync(CurrentRepository?.Path, Changes.BranchName.Length > 0 ? Changes.BranchName : null, askGitHub: true);
        Operation.MessageRaised += ShowMessage;
        Operation.NoticeRaised += Sync.ShowNotice;
        Operation.RepositoryChanged += RefreshAsync;
        Operation.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Operation.IsActive))
            {
                OnPropertyChanged(nameof(IsStatusBarVisible));
                OnPropertyChanged(nameof(IsSuccessBar));
            }

            // A rebase (from anywhere: Stacks, History, a branch) stopped on conflicts: show where to resolve them.
            if (e.PropertyName == nameof(Operation.ConflictCount) && Operation.IsActive && Operation.ConflictCount > 0 && !_conflictsShown)
            {
                _conflictsShown = true;
                SelectedTabIndex = 0;
                Changes.SelectedConflict ??= Changes.ConflictFiles.FirstOrDefault();
            }

            if (e.PropertyName == nameof(Operation.IsActive) && !Operation.IsActive)
            {
                _conflictsShown = false;
            }
        };
        Sync.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Sync.HasNotice))
            {
                OnPropertyChanged(nameof(IsStatusBarVisible));
                OnPropertyChanged(nameof(IsNoticeShown));
                OnPropertyChanged(nameof(IsSuccessBar));
            }
        };
        Branches.RebaseRequested += (onto, label) => Operation.StartRebaseAsync(BranchName, onto, label);
        History.RebaseRequested += (onto, label) => Operation.StartRebaseAsync(BranchName, onto, label);
        Branches.InteractiveRebaseRequested += onto => Operation.StartInteractiveRebaseAsync(BranchName, onto);
        History.InteractiveRebaseRequested += (commit, label) => Operation.StartInteractiveRebaseFromAsync(BranchName, commit, label);
        Changes.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Changes.ConflictCount))
            {
                Operation.ConflictCount = Changes.ConflictCount;
            }
        };
        Undo.MessageRaised += ShowMessage;
        Undo.NoticeRaised += Sync.ShowNotice;
        Undo.RepositoryChanged += RefreshAsync;
        Branches.MessageRaised += ShowMessage;
        Branches.RepositoryChanged += RefreshAsync;
        History.MessageRaised += ShowMessage;
        History.RepositoryChanged += RefreshAsync;
        Sync.MessageRaised += ShowMessage;
        Sync.RepositoryChanged += RefreshAsync;
        Changes.MessageRaised += ShowMessage;
        Changes.Committed += () => _ = RefreshAsync();
        Changes.ChangedOnDisk += RefreshAsync;

        RepositoryPicker = new RepositoryPickerViewModel(repositoryStore, repositoryService, folderPicker);
        RepositoryPicker.RepositoryOpened += item => _ = OpenRepositoryAsync(item);
        RepositoryPicker.CurrentRepositoryRemoved += CloseRepository;
        RepositoryPicker.MessageRaised += ShowMessage;
    }

    public RepositoryPickerViewModel RepositoryPicker { get; }

    public AccountViewModel Account { get; }

    public ChangesViewModel Changes { get; }

    public SyncViewModel Sync { get; }

    public History.HistoryViewModel History { get; }

    public BranchesViewModel Branches { get; }

    public UndoViewModel Undo { get; }

    public OperationViewModel Operation { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRepository), nameof(RepositoryName))]
    public partial RepositoryItemViewModel? CurrentRepository { get; set; }

    public bool HasRepository => CurrentRepository is not null;

    public string RepositoryName => CurrentRepository?.Name ?? "Choose a repository";

    /// <summary>Branch name, or a short description when HEAD is detached.</summary>
    [ObservableProperty]
    public partial string BranchName { get; set; } = "—";

    /// <summary>The pull request button in the toolbar.</summary>
    public PullRequests.PullRequestsViewModel PullRequests { get; }

    /// <summary>The Stacks tab.</summary>
    public Stacks.StacksViewModel Stacks { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage), nameof(IsStatusBarVisible), nameof(IsNoticeShown), nameof(IsSuccessBar))]
    public partial string? Message { get; set; }

    public bool HasMessage => Message is not null;

    /// <summary>The bottom bar shows while a rebase (or merge, ...) is stopped, a warning is up, or a short note.</summary>
    public bool IsStatusBarVisible => HasMessage || Operation.IsActive || Sync.HasNotice;

    /// <summary>A green note shows when there's no warning to show instead.</summary>
    public bool IsNoticeShown => Sync.HasNotice && !HasMessage;

    /// <summary>The bar is green when all it shows is a note.</summary>
    public bool IsSuccessBar => IsNoticeShown && !Operation.IsActive;

    /// <summary>0 Changes, 1 History, 2 Stacks.</summary>
    [ObservableProperty]
    public partial int SelectedTabIndex { get; set; }

    /// <summary>Reopens the repository that was open last time.</summary>
    public async Task InitializeAsync()
    {
        // Load the account in the background; the repository shouldn't wait for GitHub.
        _ = Account.InitializeAsync();

        var lastPath = _settingsStore.Settings.LastRepositoryPath;

        if (lastPath is null)
        {
            return;
        }

        var item = RepositoryPicker.Find(lastPath);

        if (item is not null && !item.IsMissing)
        {
            await OpenRepositoryAsync(item);
        }
    }

    /// <summary>Re-reads repository state, e.g. when the window regains focus.</summary>
    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (CurrentRepository is null)
        {
            return;
        }

        if (CurrentRepository.IsMissing)
        {
            ShowMessage($"\"{CurrentRepository.Path}\" no longer exists.");
            CloseRepository();
            return;
        }

        try
        {
            var branch = await _repositoryService.GetCurrentBranchAsync(CurrentRepository.Path);
            BranchName = branch ?? "Detached HEAD";
            Changes.BranchName = branch ?? string.Empty;
            await Sync.RefreshAsync(CurrentRepository.Path);
            await History.RefreshAsync(CurrentRepository.Path);
            await Undo.RefreshAsync(CurrentRepository.Path);
            await Operation.RefreshAsync(CurrentRepository.Path);
            Changes.Operation = Operation.State;

            // git detaches HEAD while it rebases; say what's really going on.
            if (branch is null && Operation.State is { Kind: Core.Operations.OperationKind.Rebase, Branch: { } rebasing })
            {
                BranchName = $"{rebasing} (rebasing)";
            }
            Operation.ConflictCount = Changes.ConflictCount;
            History.CurrentBranch = branch;
            await PullRequests.RefreshAsync(CurrentRepository.Path, branch, askGitHub: branch != PullRequests.Branch);
            await Changes.RefreshAsync();
            await Stacks.RefreshAsync(CurrentRepository.Path, branch, Operation.IsActive);
        }
        catch (GitCommandException exception)
        {
            ShowMessage(exception.Message);
        }
    }

    [RelayCommand]
    private void DismissMessage() => Message = null;

    [RelayCommand]
    private void ToggleTheme() => _themeService.Toggle();

    /// <summary>Raised so the view can close the repository dropdown before a dialog opens.</summary>
    public event Action? DialogOpening;

    [RelayCommand]
    private async Task CloneAsync()
    {
        DialogOpening?.Invoke();

        var clonedPath = await _dialogs.ShowCloneAsync();

        if (clonedPath is not null)
        {
            RepositoryPicker.AddAndOpen(clonedPath);
        }
    }

    private async Task OpenRepositoryAsync(RepositoryItemViewModel item)
    {
        CurrentRepository = item;
        RepositoryPicker.MarkCurrent(item);
        Message = null;

        _settingsStore.Settings.LastRepositoryPath = item.Path;
        _settingsStore.Save();

        await Changes.OpenAsync(item.Path);
        await RefreshAsync();
    }

    private void CloseRepository()
    {
        CurrentRepository = null;
        BranchName = "—";
        _ = Changes.OpenAsync(null);
        _ = Sync.RefreshAsync(null);
        _ = History.RefreshAsync(null);
        _ = Undo.RefreshAsync(null);
        _ = Operation.RefreshAsync(null);
        _ = PullRequests.RefreshAsync(null, null);
        _ = Stacks.RefreshAsync(null, null, operationActive: false);
        RepositoryPicker.MarkCurrent(null);

        _settingsStore.Settings.LastRepositoryPath = null;
        _settingsStore.Save();
    }

    private void ShowMessage(string message) => Message = message;
}
