using CommunityToolkit.Mvvm.Input;
using Material.Icons;
using MyGit.App.Services;

namespace MyGit.App.ViewModels;

/// <summary>
/// The window-wide extras: the command palette, keyboard shortcuts, Settings, and opening the repository
/// in your editor, terminal, Explorer or on GitHub.
/// </summary>
public sealed partial class MainWindowViewModel
{
    private IShellService? _shell;

    /// <summary>Every window-wide shortcut, for the Keyboard shortcuts list (and shown in the palette).</summary>
    public static IReadOnlyList<(string Keys, string Action)> Shortcuts { get; } =
    [
        ("Ctrl+Shift+P", "Command palette"),
        ("Ctrl+Shift+F", "Fetch"),
        ("Ctrl+Shift+L", "Pull (rebase)"),
        ("Ctrl+Shift+U", "Push"),
        ("Ctrl+Shift+N", "New branch"),
        ("Ctrl+B", "Branches (filter)"),
        ("Ctrl+R", "Repositories (filter)"),
        ("Ctrl+1 / 2 / 3", "Changes / History / Stacks"),
        ("F5", "Refresh"),
        ("Enter", "Stage or unstage the selected files"),
        ("Ctrl+A", "Select every file in the list"),
        ("Delete", "Discard the selected unstaged files"),
        ("H", "Hide or unhide the selected files"),
        ("Ctrl+H", "Show hidden files"),
        ("Ctrl+Enter", "Commit"),
    ];

    public CommandPaletteViewModel Palette { get; } = new();

    /// <summary>My Git's own updates (set by the app at startup).</summary>
    public UpdatesViewModel? Updates { get; private set; }

    /// <summary>The bottom bar's "My Git 1.2 is ready · Restart to update" (not during a stopped rebase).</summary>
    public bool IsUpdateShown => Updates?.IsReady == true && !Operation.IsActive;

    public void UseUpdates(UpdatesViewModel updates)
    {
        Updates = updates;
        updates.NoticeRaised += Sync.ShowNotice;
        updates.MessageRaised += ShowMessage;
        updates.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(UpdatesViewModel.IsReady))
            {
                OnPropertyChanged(nameof(IsUpdateShown));
                OnPropertyChanged(nameof(IsStatusBarVisible));
                OnPropertyChanged(nameof(IsSuccessBar));
            }
        };
        OnPropertyChanged(nameof(Updates));
    }

    /// <summary>"Open in VS Code" (or whichever editor Settings picks).</summary>
    public string OpenInEditorText => _shell?.EditorName is { } editor ? $"Open in {editor}" : "Open in editor (none found)";

    /// <summary>"Open in Windows Terminal".</summary>
    public string OpenInTerminalText => _shell?.TerminalName is { } terminal ? $"Open in {terminal}" : "Open in terminal (none found)";

    public bool IsOnGitHub => PullRequests.Repository is not null;

    /// <summary>Set by the app at startup (it needs the window to exist first).</summary>
    public void UseShell(IShellService shell)
    {
        _shell = shell;
        OnPropertyChanged(nameof(OpenInEditorText));
        OnPropertyChanged(nameof(OpenInTerminalText));
    }

    [RelayCommand]
    private async Task OpenPaletteAsync()
    {
        DialogOpening?.Invoke();
        if (CurrentRepository is { } repository)
        {
            await Branches.LoadAsync(repository.Path);
        }

        Palette.Open(BuildPaletteItems());
    }

    /// <summary>Ctrl+1 / 2 / 3.</summary>
    [RelayCommand]
    private void SelectTab(string index) => SelectedTabIndex = int.Parse(index, System.Globalization.CultureInfo.InvariantCulture);

    [RelayCommand]
    private async Task OpenSettingsAsync()
    {
        DialogOpening?.Invoke();
        Account.CloseMenu();
        await _dialogs.ShowSettingsAsync();
        OnPropertyChanged(nameof(OpenInEditorText));
        OnPropertyChanged(nameof(OpenInTerminalText));
    }

    [RelayCommand]
    private Task ShowShortcutsAsync()
    {
        Account.CloseMenu();
        return _dialogs.ShowShortcutsAsync();
    }

    [RelayCommand(CanExecute = nameof(HasRepository))]
    private void OpenInEditor()
    {
        DialogOpening?.Invoke();
        if (CurrentRepository is { } repository)
        {
            _shell?.OpenInEditor(repository.Path);
        }
    }

    [RelayCommand(CanExecute = nameof(HasRepository))]
    private void OpenInTerminal()
    {
        DialogOpening?.Invoke();
        if (CurrentRepository is { } repository)
        {
            _shell?.OpenTerminal(repository.Path);
        }
    }

    [RelayCommand(CanExecute = nameof(HasRepository))]
    private void ShowRepositoryInExplorer()
    {
        DialogOpening?.Invoke();
        if (CurrentRepository is { } repository)
        {
            _shell?.ShowInExplorer(repository.Path.TrimEnd('\\', '/'));
        }
    }

    [RelayCommand]
    private void OpenOnGitHub()
    {
        DialogOpening?.Invoke();
        if (PullRequests.Repository is { } gitHub)
        {
            _shell?.OpenUrl($"https://github.com/{gitHub.Owner}/{gitHub.Name}");
        }
    }

    /// <summary>Interactive rebase of the current branch onto the trunk (main), from the palette.</summary>
    [RelayCommand]
    private Task InteractiveRebaseOntoTrunkAsync() => Operation.StartInteractiveRebaseAsync(BranchName, Stacks.Trunk);

    [RelayCommand]
    private Task StashChangesAsync() => Changes.StashAsync([]);

    private List<PaletteItem> BuildPaletteItems()
    {
        var trunk = Stacks.Trunk;
        var items = new List<PaletteItem>
        {
            new("Fetch", MaterialIconKind.Refresh, Sync.FetchCommand, Shortcut: "Ctrl+Shift+F"),
            new("Pull (rebase)", MaterialIconKind.ArrowDown, Sync.PullCommand, Shortcut: "Ctrl+Shift+L"),
            new("Pull with merge", MaterialIconKind.ArrowDown, Sync.PullWithMergeCommand),
            new("Pull, fast-forward only", MaterialIconKind.ArrowDown, Sync.PullFastForwardOnlyCommand),
            new("Push", MaterialIconKind.ArrowUp, Sync.PushCommand, Shortcut: "Ctrl+Shift+U"),
            new("Force push (with lease)…", MaterialIconKind.ArrowUpBold, Sync.ForcePushCommand),
            new(PullRequests.HasPullRequest ? "Open pull request on GitHub" : "Create pull request…", MaterialIconKind.SourcePull, PullRequests.MainCommand),
            new("Undo the last branch move", MaterialIconKind.UndoVariant, Undo.UndoLastCommand),
            new("Refresh", MaterialIconKind.Refresh, RefreshCommand, Shortcut: "F5"),
            new("Go to Changes", MaterialIconKind.FileEditOutline, SelectTabCommand, "0", Shortcut: "Ctrl+1"),
            new("Go to History", MaterialIconKind.History, SelectTabCommand, "1", Shortcut: "Ctrl+2"),
            new("Go to Stacks", MaterialIconKind.LayersOutline, SelectTabCommand, "2", Shortcut: "Ctrl+3"),
            new("New branch…", MaterialIconKind.SourceBranchPlus, Branches.NewBranchCommand, Shortcut: "Ctrl+Shift+N"),
            new("Commit", MaterialIconKind.SourceCommit, Changes.CommitCommand, Shortcut: "Ctrl+Enter"),
            new("Stash changes…", MaterialIconKind.ArchiveArrowDownOutline, StashChangesCommand),
            new("Discard all changes…", MaterialIconKind.DeleteSweepOutline, Changes.DiscardAllCommand),
            new("Show hidden files", MaterialIconKind.EyeOutline, Changes.ToggleHiddenViewCommand, Shortcut: "Ctrl+H"),
            new("Switch folder tree / flat list", MaterialIconKind.FileTreeOutline, Changes.ToggleFlatCommand),
            new($"Interactive rebase onto {trunk}…", MaterialIconKind.SourceBranchSync, InteractiveRebaseOntoTrunkCommand),
            new("Continue (rebase, merge, …)", MaterialIconKind.Play, Operation.ContinueCommand),
            new("Skip this commit", MaterialIconKind.SkipNext, Operation.SkipCommand),
            new("Abort (rebase, merge, …)", MaterialIconKind.Close, Operation.AbortCommand),
            new("Stack a branch on top…", MaterialIconKind.LayersPlus, Stacks.StackOnTopCommand),
            new("New stack from existing branches…", MaterialIconKind.LayersOutline, Stacks.NewStackCommand),
            new("Restack", MaterialIconKind.LayersTripleOutline, Stacks.RestackCommand),
            new("Push stack", MaterialIconKind.ArrowUp, Stacks.PushStackCommand),
            new("Create the stack's pull requests…", MaterialIconKind.SourcePull, Stacks.CreatePullRequestsCommand),
            new("Sync stack after a merge…", MaterialIconKind.SourceMerge, Stacks.SyncStackCommand),
            new(OpenInEditorText, MaterialIconKind.CodeBraces, OpenInEditorCommand),
            new(OpenInTerminalText, MaterialIconKind.Console, OpenInTerminalCommand),
            new("Show in Explorer", MaterialIconKind.FolderOpenOutline, ShowRepositoryInExplorerCommand),
            new("Open on GitHub", MaterialIconKind.Github, OpenOnGitHubCommand),
            new("Clone a repository…", MaterialIconKind.CloudDownloadOutline, CloneCommand),
            new("Add a local repository…", MaterialIconKind.FolderPlusOutline, RepositoryPicker.AddLocalCommand),
            new("Switch theme (dark / light)", MaterialIconKind.ThemeLightDark, ToggleThemeCommand),
            new("Settings…", MaterialIconKind.CogOutline, OpenSettingsCommand),
            new("Keyboard shortcuts", MaterialIconKind.KeyboardOutline, ShowShortcutsCommand),
            new($"Check for updates ({Updates?.Version ?? "dev build"})", MaterialIconKind.Update, Updates?.CheckNowCommand ?? RefreshCommand),
            Account.IsSignedIn
                ? new("Sign out of GitHub", MaterialIconKind.Logout, Account.SignOutCommand)
                : new("Sign in to GitHub…", MaterialIconKind.Github, Account.SignInCommand),
        };

        // Branches to switch to, and repositories to open.
        items.AddRange(Branches.Local.Where(branch => !branch.IsCurrent)
            .Select(branch => new PaletteItem($"Switch to {branch.Name}", MaterialIconKind.SourceBranch, branch.SwitchCommand, Hint: "branch")));
        items.AddRange(Branches.Remote
            .Select(branch => new PaletteItem($"Check out {branch.Name}", MaterialIconKind.CloudOutline, branch.CheckoutCommand, Hint: "GitHub branch")));
        items.AddRange(RepositoryPicker.Items.Where(repository => !repository.IsCurrent)
            .Select(repository => new PaletteItem($"Open repository {repository.Name}", MaterialIconKind.FolderOutline, repository.OpenCommand, Hint: "repository")));

        return items;
    }
}
