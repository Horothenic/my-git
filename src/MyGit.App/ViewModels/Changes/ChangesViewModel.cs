using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyGit.App.Services;
using MyGit.Core.Changes;
using MyGit.Core.Commits;
using MyGit.Core.Diffs;
using MyGit.Core.Git;
using MyGit.Core.Settings;

namespace MyGit.App.ViewModels.Changes;

/// <summary>
/// The Changes tab: Unstaged and Staged trees, the diff of the selected file, and the commit box.
/// Actions take the files selected in a tree; the view passes them in (see ChangesView.axaml.cs).
/// </summary>
public sealed partial class ChangesViewModel : ObservableObject, IDisposable
{
    private readonly ChangesService _changes;
    private readonly PartialChangesService _partial;
    private readonly Core.Stashes.StashService _stashes;
    private readonly Core.Operations.ConflictService _conflicts;
    private readonly Core.Operations.MergeToolService _mergeTools;
    private readonly CommitService _commits;
    private readonly HiddenFilesStore _hidden;
    private readonly Core.Repositories.RepositoryService _repositories;
    private readonly AppSettingsStore _settings;
    private readonly IDialogService _dialogs;
    private readonly IShellService _shell;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    private RepositoryWatcher? _watcher;
    private WorkingTreeStatus _status = WorkingTreeStatus.Empty;
    private bool _refreshPending;
    private (string Summary, string Description) _messageBeforeAmend;

    // For the green strip: the branch it went to, and the message Undo puts back into the box.
    private string _lastCommitBranch = string.Empty;
    private (string Summary, string Description) _lastCommitMessage;

    // Which hidden list applies: the repo's GitHub address, or its folder if it has no remote.
    private string _hiddenKey = string.Empty;

    public ChangesViewModel(
        ChangesService changes,
        PartialChangesService partial,
        Core.Stashes.StashService stashes,
        Core.Operations.ConflictService conflicts,
        Core.Operations.MergeToolService mergeTools,
        CommitService commits,
        Core.Diffs.DiffService diffs,
        HiddenFilesStore hidden,
        Core.Repositories.RepositoryService repositories,
        AppSettingsStore settings,
        IDialogService dialogs,
        IShellService shell)
    {
        _changes = changes;
        _partial = partial;
        _stashes = stashes;
        _conflicts = conflicts;
        _mergeTools = mergeTools;
        _commits = commits;
        _hidden = hidden;
        _repositories = repositories;
        _settings = settings;
        _dialogs = dialogs;
        _shell = shell;

        Diff = new DiffViewModel(diffs);
        Diff.ActionRequested += (action, lines) => ApplyLinesAsync(lines, discard: action == DiffAction.Discard);
        IsFlat = settings.Settings.FlatFileList;
    }

    /// <summary>Raised with an error message for the main window's message bar.</summary>
    public event Action<string>? MessageRaised;

    /// <summary>Raised when the repository changed on disk; the main window refreshes everything.</summary>
    public event Func<Task>? ChangedOnDisk;

    /// <summary>Raised after a commit, so the main window can refresh branch info.</summary>
    public event Action? Committed;

    public string? RepositoryPath { get; private set; }

    public FileTree Unstaged { get; } = new();

    public FileTree Staged { get; } = new();

    public DiffViewModel Diff { get; }

    // ---- Conflicts (while a rebase, merge, cherry-pick or revert is stopped) ----

    public System.Collections.ObjectModel.ObservableCollection<FileChange> ConflictFiles { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasConflicts))]
    public partial int ConflictCount { get; set; }

    public bool HasConflicts => ConflictCount > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConflictSelected), nameof(IsDiffShown))]
    public partial FileChange? SelectedConflict { get; set; }

    public bool IsConflictSelected => SelectedConflict is not null;

    /// <summary>The built-in resolver for the selected conflict (active for text files with conflict markers).</summary>
    public ConflictResolverViewModel Resolver { get; } = new();

    /// <summary>The normal diff, unless a conflicted file is selected.</summary>
    public bool IsDiffShown => !IsConflictSelected;

    /// <summary>The stopped operation, set by the window. Used to name the two sides clearly.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OursLabel), nameof(TheirsLabel), nameof(UseOursText), nameof(UseTheirsText),
        nameof(OursExplanation), nameof(TheirsExplanation), nameof(IsCommitBoxVisible), nameof(IsOperationActive))]
    public partial Core.Operations.OperationState Operation { get; set; } = Core.Operations.OperationState.Idle;

    /// <summary>During a rebase / merge / cherry-pick / revert, Continue (in the banner) commits; the box is hidden.</summary>
    public bool IsCommitBoxVisible => !Operation.IsActive;

    public bool IsOperationActive => Operation.IsActive;

    public string OursExplanation => $"{UseOursText} keeps the version from {OursLabel}.";

    public string TheirsExplanation => $"{UseTheirsText} keeps the version from {TheirsLabel}.";

    // git's "ours" and "theirs" swap meaning in a rebase: name them by what they are.
    public string OursLabel => Operation.Kind == Core.Operations.OperationKind.Rebase ? $"{Operation.Onto ?? "the target branch"} (rebasing onto)" : "yours (current branch)";

    public string TheirsLabel => Operation.Kind switch
    {
        Core.Operations.OperationKind.Rebase => $"yours: “{Operation.CurrentCommit}”",
        Core.Operations.OperationKind.Merge => "the branch being merged in",
        Core.Operations.OperationKind.CherryPick => $"the picked commit “{Operation.CurrentCommit}”",
        Core.Operations.OperationKind.Revert => $"the revert of “{Operation.CurrentCommit}”",
        _ => "theirs",
    };

    public string UseOursText => Operation.Kind == Core.Operations.OperationKind.Rebase ? $"Use {Operation.Onto ?? "target"}'s" : "Use yours";

    public string UseTheirsText => Operation.Kind switch
    {
        Core.Operations.OperationKind.Rebase => "Use yours",
        Core.Operations.OperationKind.Merge => "Use theirs",
        _ => "Use the commit's",
    };

    /// <summary>Merge tools that fit the selected conflict (UnityYAMLMerge only for Unity assets).</summary>
    public System.Collections.ObjectModel.ObservableCollection<Core.Operations.MergeTool> MergeTools { get; } = [];

    public bool HasMergeTools => MergeTools.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotRunningTool))]
    public partial string? RunningTool { get; set; }

    public bool IsNotRunningTool => RunningTool is null;

    partial void OnSelectedConflictChanged(FileChange? value)
    {
        _ = LoadMergeToolsAsync(value);
        _ = LoadResolverAsync(value);
    }

    private async Task LoadResolverAsync(FileChange? conflict)
    {
        if (conflict is null || RepositoryPath is not { } repository)
        {
            Resolver.Clear();
            return;
        }

        var fullPath = Path.Combine(repository, conflict.Path.Replace('/', Path.DirectorySeparatorChar));
        var parsed = File.Exists(fullPath) ? Core.Operations.ConflictFile.Parse(await File.ReadAllTextAsync(fullPath)) : null;

        // Deleted on one side, binary, or no markers: the whole-file buttons are the way to go.
        if (parsed is null)
        {
            Resolver.Clear();
            return;
        }

        var labels = (Ours: OursLabel, Theirs: TheirsLabel);
        Resolver.Load(parsed, labels.Ours, labels.Theirs, result => SaveResolvedAsync(conflict, fullPath, result));
    }

    private async Task SaveResolvedAsync(FileChange conflict, string fullPath, string result)
    {
        await File.WriteAllTextAsync(fullPath, result, new System.Text.UTF8Encoding(false));

        if (Core.Operations.ConflictFile.Parse(result) is not null || Core.Operations.ConflictService.HasConflictMarkers(fullPath))
        {
            MessageRaised?.Invoke($"Saved {conflict.FileName}, but some conflicts are still undecided, so it stays under Conflicts.");
            return;
        }

        await RunAsync(repository => _conflicts.MarkResolvedAsync(repository, conflict.Path));
    }

    [RelayCommand]
    private Task UseOursAsync() => ResolveSelectedAsync(path => _conflicts.UseSideAsync(RepositoryPath!, path, Core.Operations.ConflictSide.Ours));

    [RelayCommand]
    private Task UseTheirsAsync() => ResolveSelectedAsync(path => _conflicts.UseSideAsync(RepositoryPath!, path, Core.Operations.ConflictSide.Theirs));

    [RelayCommand]
    private Task MarkResolvedAsync()
    {
        if (SelectedConflict is { } conflict && RepositoryPath is { } repository &&
            Core.Operations.ConflictService.HasConflictMarkers(Path.Combine(repository, conflict.Path.Replace('/', Path.DirectorySeparatorChar))))
        {
            MessageRaised?.Invoke($"{conflict.FileName} still has conflict markers (<<<<<<<). Remove them first, or use one of the other buttons.");
            return Task.CompletedTask;
        }

        return ResolveSelectedAsync(path => _conflicts.MarkResolvedAsync(RepositoryPath!, path));
    }

    [RelayCommand]
    private void OpenConflictInDefaultApp()
    {
        if (SelectedConflict is { } conflict && RepositoryPath is { } repository)
        {
            _shell.OpenFile(Path.Combine(repository, conflict.Path.Replace('/', Path.DirectorySeparatorChar)));
        }
    }

    [RelayCommand]
    private async Task RunMergeToolAsync(Core.Operations.MergeTool tool)
    {
        if (SelectedConflict is not { } conflict || RepositoryPath is not { } repository)
        {
            return;
        }

        RunningTool = $"Waiting for {tool.Name} to close…";

        try
        {
            if (await _mergeTools.RunAsync(repository, conflict.Path, tool) == Core.Operations.MergeToolOutcome.NotResolved)
            {
                MessageRaised?.Invoke($"{tool.Name} closed without a finished result, so {conflict.FileName} is still conflicted.");
            }
        }
        catch (GitCommandException exception)
        {
            MessageRaised?.Invoke(exception.Message);
        }
        finally
        {
            RunningTool = null;
        }

        await RefreshAsync();
    }

    private async Task ResolveSelectedAsync(Func<string, Task> resolve)
    {
        if (SelectedConflict is not { } conflict || RepositoryPath is null)
        {
            return;
        }

        await RunAsync(_ => resolve(conflict.Path));
    }

    private async Task LoadMergeToolsAsync(FileChange? conflict)
    {
        MergeTools.Clear();

        if (conflict is not null && RepositoryPath is { } repository)
        {
            foreach (var tool in await _mergeTools.GetToolsForAsync(repository, conflict.Path))
            {
                MergeTools.Add(tool);
            }
        }

        OnPropertyChanged(nameof(HasMergeTools));
    }

    // ---- Stashes ----

    public System.Collections.ObjectModel.ObservableCollection<StashItemViewModel> Stashes { get; } = [];

    public int StashCount => Stashes.Count;

    public bool HasStashes => Stashes.Count > 0;

    [ObservableProperty]
    public partial bool IsStashesExpanded { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedStash))]
    public partial StashItemViewModel? SelectedStash { get; set; }

    public bool HasSelectedStash => SelectedStash is not null;

    /// <summary>Files of the selected stash.</summary>
    public System.Collections.ObjectModel.ObservableCollection<FileChange> StashFiles { get; } = [];

    [ObservableProperty]
    public partial FileChange? SelectedStashFile { get; set; }

    /// <summary>Branch name for the commit button, set by the main window.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CommitButtonText))]
    public partial string BranchName { get; set; } = string.Empty;

    /// <summary>When true, the Unstaged list shows only hidden files (Ctrl+H or the eye button).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UnstagedTitle), nameof(IsShowingChanges), nameof(UnstagedEmptyText))]
    public partial bool IsShowingHidden { get; set; }

    public bool IsShowingChanges => !IsShowingHidden;

    public string UnstagedTitle => IsShowingHidden ? "Hidden" : "Unstaged";

    public string UnstagedEmptyText => IsShowingHidden ? "Nothing hidden. Select files and press H to hide them." : "No changes";

    [ObservableProperty]
    public partial bool IsFlat { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUnstagedEmpty), nameof(ChangedFileCount), nameof(HasChangedFiles))]
    public partial int UnstagedCount { get; set; }

    public bool IsUnstagedEmpty => UnstagedCount == 0;

    public bool IsStagedEmpty => StagedCount == 0;

    /// <summary>For the count on the Changes tab: files in either list (hidden ones don't count).</summary>
    public int ChangedFileCount => _visibleUnstagedCount + StagedCount;

    // Unstaged files that aren't hidden, whichever list is showing.
    private int _visibleUnstagedCount;

    public bool HasChangedFiles => ChangedFileCount > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StagedCountText), nameof(CanCommit), nameof(IsStagedEmpty), nameof(ChangedFileCount), nameof(HasChangedFiles))]
    [NotifyCanExecuteChangedFor(nameof(CommitCommand))]
    public partial int StagedCount { get; set; }

    [ObservableProperty]
    public partial int HiddenCount { get; set; }

    public string StagedCountText => StagedCount == 1 ? "1 file staged" : $"{StagedCount} files staged";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCommit))]
    [NotifyCanExecuteChangedFor(nameof(CommitCommand))]
    public partial string Summary { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Description { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCommit), nameof(CommitButtonText))]
    [NotifyCanExecuteChangedFor(nameof(CommitCommand))]
    public partial bool IsAmend { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCommit), nameof(CommitButtonText), nameof(IsNotCommitting))]
    [NotifyCanExecuteChangedFor(nameof(CommitCommand))]
    public partial bool IsCommitting { get; set; }

    public bool IsNotCommitting => !IsCommitting;

    public bool CanCommit => !IsCommitting && Summary.Trim().Length > 0 && (StagedCount > 0 || IsAmend);

    public string CommitButtonText =>
        IsCommitting ? "Committing…" :
        IsAmend ? "Amend last commit" :
        string.IsNullOrEmpty(BranchName) ? "Commit" : $"Commit to {BranchName}";

    /// <summary>The commit just made from this box, shown in the green strip with Undo.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLastCommit), nameof(LastCommitTitle), nameof(LastCommitDetails))]
    public partial CommitInfo? LastCommit { get; set; }

    public bool HasLastCommit => LastCommit is not null;

    public string LastCommitTitle => LastCommit is null
        ? string.Empty
        : $"{(LastCommit.WasAmend ? "Amended" : "Committed")} “{LastCommit.Summary}” to {_lastCommitBranch}";

    public string LastCommitDetails => LastCommit is null
        ? string.Empty
        : $"{LastCommit.ShortSha}  ·  {(LastCommit.FileCount == 1 ? "1 file" : $"{LastCommit.FileCount} files")}  ·  {LastCommit.CommittedAt:t}";

    /// <summary>Starts showing a repository (or nothing, for null).</summary>
    public async Task OpenAsync(string? repositoryPath)
    {
        _watcher?.Dispose();
        _watcher = null;

        RepositoryPath = repositoryPath;
        IsShowingHidden = false;
        IsAmend = false;
        LastCommit = null;
        Summary = string.Empty;
        Description = string.Empty;
        await Diff.ShowAsync(repositoryPath ?? string.Empty, null, staged: false);

        if (repositoryPath is null)
        {
            _status = WorkingTreeStatus.Empty;
            UpdateTrees();
            return;
        }

        // Hidden files follow the repo's GitHub address, so moving or re-cloning keeps them.
        // Lists saved under the folder path (before this, or before a remote existed) move over.
        var remote = await _repositories.GetRemoteUrlAsync(repositoryPath);
        _hiddenKey = HiddenFilesStore.KeyFor(repositoryPath, remote);
        _hidden.MoveKey(repositoryPath, _hiddenKey);

        _watcher = new RepositoryWatcher(repositoryPath);
        // Files changed on disk (an editor, Unity, or git in a terminal). The main window refreshes
        // everything (branch, sync counts and these lists); without a listener, just the lists.
        _watcher.Changed += () => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            _ = ChangedOnDisk is { } handler ? handler() : RefreshAsync());

        await RefreshAsync();
    }

    /// <summary>Re-reads git status. Safe to call often: overlapping calls are merged.</summary>
    public async Task RefreshAsync()
    {
        if (RepositoryPath is not { } repository)
        {
            return;
        }

        if (!await _refreshGate.WaitAsync(0))
        {
            // A refresh is running; run once more when it's done so nothing is missed.
            _refreshPending = true;
            return;
        }

        try
        {
            do
            {
                _refreshPending = false;
                _status = await _changes.GetStatusAsync(repository);

                if (RepositoryPath != repository)
                {
                    return; // the user switched repositories meanwhile
                }

                UpdateTrees();
                await RefreshDiffAsync();
                await RefreshStashesAsync(repository);
            }
            while (_refreshPending);
        }
        catch (GitCommandException exception)
        {
            MessageRaised?.Invoke(exception.Message);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    /// <summary>Shows the diff for a file picked in one of the trees.</summary>
    public Task SelectFileAsync(FileTreeNodeViewModel? node, bool staged) =>
        Diff.ShowAsync(RepositoryPath ?? string.Empty, node?.IsFile == true ? node.Change : null, staged);

    // ---- Actions on the selection (called from the view) ----

    /// <summary>Enter / double-click: Unstaged → Staged, or Staged → Unstaged.</summary>
    public Task ToggleStageAsync(IReadOnlyCollection<FileTreeNodeViewModel> selection, bool fromStaged) =>
        fromStaged ? UnstageAsync(selection) : StageAsync(selection);

    public Task StageAsync(IReadOnlyCollection<FileTreeNodeViewModel> selection) =>
        RunAsync(repository => _changes.StageAsync(repository, PathsOf(selection)));

    public Task UnstageAsync(IReadOnlyCollection<FileTreeNodeViewModel> selection) =>
        RunAsync(repository => _changes.UnstageAsync(repository, PathsOf(selection)));

    /// <summary>Del: throw away unstaged changes, after asking.</summary>
    public async Task DiscardAsync(IReadOnlyCollection<FileTreeNodeViewModel> selection)
    {
        var files = FilesOf(selection).Select(file => file.Change!).ToList();
        if (files.Count == 0 || RepositoryPath is null)
        {
            return;
        }

        var untracked = files.Count(file => file.Kind == ChangeKind.Untracked);
        var what = files.Count == 1 ? files[0].Path : $"{files.Count} files";
        var detail = untracked == 0
            ? "Your changes will be lost. This can't be undone."
            : untracked == files.Count
                ? "New files are moved to the Recycle Bin."
                : "Changes to existing files are lost and can't be undone. New files are moved to the Recycle Bin.";

        if (await _dialogs.ConfirmAsync($"Discard changes to {what}?", detail, "Discard"))
        {
            await RunAsync(repository => _changes.DiscardAsync(repository, files));
        }
    }

    /// <summary>H: hide the selection, or unhide it when the hidden list is showing.</summary>
    public async Task ToggleHiddenAsync(IReadOnlyCollection<FileTreeNodeViewModel> selection)
    {
        if (RepositoryPath is null || selection.Count == 0)
        {
            return;
        }

        var entries = selection.Select(node => node.IsFolder ? node.Path + "/" : node.Path).ToList();

        if (IsShowingHidden)
        {
            _hidden.Unhide(_hiddenKey, entries);
        }
        else
        {
            _hidden.Hide(_hiddenKey, entries);
        }

        UpdateTrees();
        await RefreshDiffAsync();
    }

    [RelayCommand]
    private Task StageAllAsync() => StageAsync(Unstaged.Roots.ToList());

    [RelayCommand]
    private Task UnstageAllAsync() => UnstageAsync(Staged.Roots.ToList());

    [RelayCommand]
    private async Task ToggleHiddenViewAsync()
    {
        IsShowingHidden = !IsShowingHidden;
        UpdateTrees();
        await RefreshDiffAsync();
    }

    [RelayCommand]
    private void ToggleFlat()
    {
        IsFlat = !IsFlat;
        _settings.Settings.FlatFileList = IsFlat;
        _settings.Save();
        UpdateTrees();
    }

    /// <summary>Stage or unstage the file shown in the diff (buttons in the diff header).</summary>
    [RelayCommand]
    private Task StageShownFileAsync() =>
        Diff.Change is { } change ? RunAsync(repository => Diff.IsStaged
            ? _changes.UnstageAsync(repository, [change.Path])
            : _changes.StageAsync(repository, [change.Path])) : Task.CompletedTask;

    [RelayCommand]
    private Task DiscardShownFileAsync() =>
        Diff.Change is { } change && Unstaged.FindFile(change.Path) is { } node ? DiscardAsync([node]) : Task.CompletedTask;

    // ---- Hunks and lines (Phase 3b) ----

    private async Task ApplyLinesAsync(IReadOnlySet<DiffLineRef> lines, bool discard)
    {
        if (RepositoryPath is not { } repository || Diff.Change is not { } change || Diff.CurrentDiff is not { } shown || lines.Count == 0)
        {
            return;
        }

        if (discard)
        {
            if (Diff.IsStaged)
            {
                return; // staged changes are unstaged first, never discarded directly
            }

            var what = lines.Count == 1 ? "this line" : $"these {lines.Count} lines";
            if (!await _dialogs.ConfirmAsync($"Discard {what}?", "Those changes will be lost. This can't be undone.", "Discard"))
            {
                return;
            }
        }

        try
        {
            if (discard)
            {
                await _partial.DiscardAsync(repository, change, shown, lines);
            }
            else if (Diff.IsStaged)
            {
                await _partial.UnstageAsync(repository, change, shown, lines);
            }
            else
            {
                await _partial.StageAsync(repository, change, shown, lines);
            }
        }
        catch (StaleDiffException exception)
        {
            MessageRaised?.Invoke(exception.Message);
        }
        catch (Exception exception) when (exception is GitCommandException or IOException or UnauthorizedAccessException)
        {
            MessageRaised?.Invoke(exception is GitCommandException git && !string.IsNullOrWhiteSpace(git.Result.StandardError)
                ? $"Couldn't apply that change: {git.Result.StandardError.Trim()}"
                : exception.Message);
        }

        await RefreshAsync();
    }

    /// <summary>The Unstaged header's Stash… button; with files selected, offers to stash only those.</summary>
    public async Task StashAsync(IReadOnlyCollection<FileTreeNodeViewModel> selection)
    {
        if (RepositoryPath is not { } repository || _status.Unstaged.Count + _status.Staged.Count == 0)
        {
            return;
        }

        var selectedPaths = PathsOf(selection);
        var answer = await _dialogs.AskNameAsync(new NameDialogOptions(
            "Stash changes",
            "Puts your uncommitted changes aside (new files too), so the working tree is clean. Bring them back from Stashes.",
            $"WIP on {BranchName}",
            "Stash",
            _ => Task.FromResult<string?>(null),
            CheckBoxText: selectedPaths.Count > 0 ? $"Only the {selectedPaths.Count} selected file(s)" : null,
            AllowEmpty: true));

        if (answer is null)
        {
            return;
        }

        var message = answer.Name.Length > 0 ? answer.Name : null;
        await RunAsync(async path =>
        {
            if (!await _stashes.PushAsync(path, message, answer.IsChecked ? selectedPaths : null))
            {
                MessageRaised?.Invoke("There was nothing to stash.");
            }
        });
    }

    public async Task ApplyStashAsync(StashItemViewModel item, bool remove)
    {
        if (RepositoryPath is null)
        {
            return;
        }

        await RunAsync(async path =>
        {
            switch (await _stashes.ApplyAsync(path, item.Stash, remove))
            {
                case Core.Stashes.StashApplyOutcome.Blocked:
                    MessageRaised?.Invoke("Your current changes are in the way of that stash. Commit or stash them first. Nothing was changed.");
                    break;
                case Core.Stashes.StashApplyOutcome.Conflict:
                    MessageRaised?.Invoke("The stash was applied, but some files have conflicts to resolve. The stash is kept, just in case. " +
                                          "A conflict screen comes in a later version.");
                    break;
            }
        });
    }

    public async Task DropStashAsync(StashItemViewModel item)
    {
        if (RepositoryPath is null ||
            !await _dialogs.ConfirmAsync($"Delete the stash “{item.Message}”?", "Its changes will be gone. This can't be undone.", "Delete stash"))
        {
            return;
        }

        await RunAsync(path => _stashes.DropAsync(path, item.Stash));
    }

    partial void OnSelectedStashChanged(StashItemViewModel? value) => _ = LoadStashFilesAsync(value);

    partial void OnSelectedStashFileChanged(FileChange? value)
    {
        if (RepositoryPath is { } repository && SelectedStash is { } stash && value is not null)
        {
            _ = Diff.ShowCommitFileAsync(repository, value, token => _stashes.GetFileDiffAsync(repository, stash.Stash, value, token));
        }
    }

    private async Task LoadStashFilesAsync(StashItemViewModel? stash)
    {
        StashFiles.Clear();

        if (stash is null || RepositoryPath is not { } repository)
        {
            return;
        }

        try
        {
            foreach (var file in await _stashes.GetFilesAsync(repository, stash.Stash))
            {
                StashFiles.Add(file);
            }
        }
        catch (GitCommandException exception)
        {
            MessageRaised?.Invoke(exception.Message);
        }
    }

    private async Task RefreshStashesAsync(string repository)
    {
        var list = await _stashes.ListAsync(repository);

        // Only rebuild when something changed, so the selection survives auto-refresh.
        if (list.Select(s => s.Sha).SequenceEqual(Stashes.Select(s => s.Stash.Sha)))
        {
            return;
        }

        var selectedSha = SelectedStash?.Stash.Sha;
        Stashes.Clear();
        foreach (var stash in list)
        {
            Stashes.Add(new StashItemViewModel(stash, this));
        }

        SelectedStash = Stashes.FirstOrDefault(s => s.Stash.Sha == selectedSha);
        OnPropertyChanged(nameof(StashCount));
        OnPropertyChanged(nameof(HasStashes));
    }

    public void OpenInEditor(FileTreeNodeViewModel node) => _shell.OpenFile(FullPath(node));

    public void ShowInExplorer(FileTreeNodeViewModel node) => _shell.ShowInExplorer(FullPath(node));

    public Task CopyPathAsync(FileTreeNodeViewModel node) => _shell.CopyTextAsync(node.Path);

    // ---- Commit ----

    partial void OnIsAmendChanged(bool value) => _ = LoadAmendMessageAsync(value);

    [RelayCommand(CanExecute = nameof(CanCommit))]
    private async Task CommitAsync()
    {
        if (RepositoryPath is not { } repository || !CanCommit)
        {
            return;
        }

        IsCommitting = true;

        try
        {
            var message = (Summary, Description);
            var commit = await _commits.CommitAsync(repository, CommitService.BuildMessage(Summary, Description), IsAmend);

            _lastCommitMessage = message;
            _lastCommitBranch = BranchName;
            LastCommit = commit;

            // Clear the box. Reset the amend memory first so unticking Amend doesn't bring text back.
            _messageBeforeAmend = (string.Empty, string.Empty);
            IsAmend = false;
            Summary = string.Empty;
            Description = string.Empty;
            Committed?.Invoke();
        }
        catch (GitCommandException exception)
        {
            // Usually a commit hook said no; its output explains why.
            MessageRaised?.Invoke(string.IsNullOrWhiteSpace(exception.Result.StandardError) ? exception.Message : exception.Result.StandardError.Trim());
        }
        finally
        {
            IsCommitting = false;
            await RefreshAsync();
        }
    }

    /// <summary>The × on the green strip: hide it. The commit itself stays.</summary>
    [RelayCommand]
    private void DismissLastCommit() => LastCommit = null;

    /// <summary>Takes back the commit in the green strip: its files return to Staged, its message to the box.</summary>
    [RelayCommand]
    private async Task UndoLastCommitAsync()
    {
        if (RepositoryPath is not { } repository || LastCommit is not { } commit)
        {
            return;
        }

        try
        {
            if (await _commits.UndoAsync(repository, commit) is { } reason)
            {
                MessageRaised?.Invoke(reason);
                return;
            }

            LastCommit = null;
            (Summary, Description) = _lastCommitMessage;
            Committed?.Invoke(); // the main window refreshes branch info, same as after a commit
        }
        catch (GitCommandException exception)
        {
            MessageRaised?.Invoke(exception.Message);
        }
        finally
        {
            await RefreshAsync();
        }
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _refreshGate.Dispose();
    }

    private async Task LoadAmendMessageAsync(bool amend)
    {
        if (RepositoryPath is not { } repository)
        {
            return;
        }

        if (amend)
        {
            _messageBeforeAmend = (Summary, Description);
            if (await _commits.GetLastMessageAsync(repository) is { } last)
            {
                (Summary, Description) = CommitService.SplitMessage(last);
            }
        }
        else
        {
            (Summary, Description) = _messageBeforeAmend;
        }
    }

    // ---- Helpers ----

    private void UpdateTrees()
    {
        // Conflicts get their own list above Unstaged, and are never hidden.
        var conflicts = _status.Unstaged.Where(change => change.Kind == ChangeKind.Conflicted).ToList();
        var others = _status.Unstaged.Where(change => change.Kind != ChangeKind.Conflicted).ToList();
        UpdateConflicts(conflicts);

        var hidden = others.Where(change => _hidden.IsHidden(_hiddenKey, change.Path)).ToList();
        var visible = others.Where(change => !_hidden.IsHidden(_hiddenKey, change.Path)).ToList();

        Unstaged.Update(IsShowingHidden ? hidden : visible, IsFlat);
        Staged.Update(_status.Staged, IsFlat);

        _visibleUnstagedCount = visible.Count;
        UnstagedCount = Unstaged.FileCount;
        StagedCount = Staged.FileCount;
        HiddenCount = hidden.Count;
    }

    private void UpdateConflicts(List<FileChange> conflicts)
    {
        var selected = SelectedConflict?.Path;

        if (!conflicts.Select(c => c.Path).SequenceEqual(ConflictFiles.Select(c => c.Path)))
        {
            ConflictFiles.Clear();
            foreach (var conflict in conflicts)
            {
                ConflictFiles.Add(conflict);
            }
        }

        // Selection first: the window may open the first conflict when the count changes.
        SelectedConflict = ConflictFiles.FirstOrDefault(c => c.Path == selected);
        ConflictCount = conflicts.Count;
    }

    /// <summary>Keeps the diff in sync after a refresh: reload it, or clear it if the file left its list.</summary>
    private Task RefreshDiffAsync()
    {
        // Nothing shown, or a stash file (read-only): a working-tree refresh doesn't affect it.
        if (Diff.Change is not { } shown || Diff.IsReadOnly)
        {
            return Task.CompletedTask;
        }

        var node = (Diff.IsStaged ? Staged : Unstaged).FindFile(shown.Path);
        return Diff.ShowAsync(RepositoryPath ?? string.Empty, node?.Change, Diff.IsStaged);
    }

    private async Task RunAsync(Func<string, Task> action)
    {
        if (RepositoryPath is not { } repository)
        {
            return;
        }

        try
        {
            await action(repository);
        }
        catch (Exception exception) when (exception is GitCommandException or IOException or UnauthorizedAccessException)
        {
            MessageRaised?.Invoke(exception.Message);
        }

        await RefreshAsync();
    }

    private static List<FileTreeNodeViewModel> FilesOf(IEnumerable<FileTreeNodeViewModel> selection) =>
        selection.SelectMany(node => node.Files()).Where(file => file.Change is not null).DistinctBy(file => file.Path).ToList();

    private static List<string> PathsOf(IEnumerable<FileTreeNodeViewModel> selection) =>
        FilesOf(selection).SelectMany(file => file.Change!.OriginalPath is { } original ? new[] { original, file.Path } : [file.Path]).Distinct().ToList();

    private string FullPath(FileTreeNodeViewModel node) =>
        Path.Combine(RepositoryPath ?? string.Empty, node.Path.Replace('/', Path.DirectorySeparatorChar));
}
