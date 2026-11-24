using Avalonia.Input;
using Avalonia.Controls;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MyGit.App.Services;
using MyGit.App.ViewModels;
using MyGit.App.ViewModels.Changes;
using MyGit.App.Views;
using MyGit.Core.Changes;
using MyGit.Core.Clone;
using MyGit.Core.Commits;
using MyGit.Core.Diffs;
using MyGit.Core.Git;
using MyGit.Core.Repositories;
using MyGit.Core.Settings;
using MyGit.Core.Sync;
using MyGit.Core.History;
using MyGit.Core.Branches;
using MyGit.App.ViewModels.History;
using MyGit.GitHub.Accounts;
using MyGit.GitHub.Repositories;

namespace MyGit.App;

public sealed class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Plain constructor wiring for now; a DI container can come later if it earns its keep.
            // The token lives in the Keychain on a Mac, in Credential Manager on Windows.
            ITokenStore tokenStore = OperatingSystem.IsWindowsVersionAtLeast(5, 1, 2600) ? new WindowsCredentialTokenStore()
                : OperatingSystem.IsMacOS() ? new MacKeychainTokenStore()
                : throw new PlatformNotSupportedException("My Git runs on Windows and macOS.");
            var accounts = new AccountManager(tokenStore);
            var git = new GitRunner(accounts); // every git command gets the GitHub token
            var settingsStore = new AppSettingsStore(AppSettingsStore.DefaultFilePath);
            var repositoryStore = new RepositoryStore(RepositoryStore.DefaultFilePath);
            var repositoryService = new RepositoryService(git);
            var cloneService = new CloneService(git);
            var gitHubRepositories = new GitHubRepositoryService(accounts);
            var deviceFlow = new DeviceFlowSignIn();
            var changesService = new ChangesService(git);
            var commitService = new CommitService(git);
            var diffService = new DiffService(git);
            var hiddenFiles = new HiddenFilesStore(HiddenFilesStore.DefaultFilePath);
            var historyService = new HistoryService(git);

            var themeService = new ThemeService(settingsStore);
            themeService.ApplySaved();

            var window = new MainWindow();
            var shell = new ShellService(window, settingsStore);

            // Settings needs parts of the main view model, which needs the dialogs: filled in right below.
            MainWindowViewModel? main = null;
            var dialogs = new DialogService(
                window,
                createSignIn: () => new SignInViewModel(deviceFlow, accounts, shell),
                createClone: folderPicker => new CloneViewModel(
                    cloneService, gitHubRepositories, accounts, repositoryStore, repositoryService, settingsStore, folderPicker),
                createSettings: folderPicker => new SettingsViewModel(
                    settingsStore, themeService, main!.Account, folderPicker, flat => main!.Changes.SetFlat(flat)));

            var viewModel = new MainWindowViewModel(
                repositoryStore,
                repositoryService,
                settingsStore,
                themeService,
                new FolderPicker(window),
                dialogs,
                new AccountViewModel(accounts, dialogs, shell),
                new ChangesViewModel(changesService, new PartialChangesService(git, changesService, diffService), new Core.Stashes.StashService(git), new Core.Operations.ConflictService(git), new Core.Operations.MergeToolService(git), commitService, diffService, hiddenFiles, repositoryService, settingsStore, dialogs, shell),
                new SyncViewModel(new SyncService(git), dialogs),
                new HistoryViewModel(historyService, new CommitActionsService(git), git, diffService, dialogs, shell),
                new BranchesViewModel(new BranchService(git), historyService, dialogs),
                new UndoViewModel(new MyGit.Core.Undo.UndoService(git), dialogs),
                new OperationViewModel(new MyGit.Core.Operations.OperationService(git), new MyGit.Core.Operations.RebaseService(git), dialogs),
                new ViewModels.PullRequests.PullRequestsViewModel(
                    new MyGit.GitHub.PullRequests.PullRequestService(accounts), new MyGit.Core.PullRequests.PullRequestDraftService(git), repositoryService, dialogs, shell),
                (pullRequests, branches) => new ViewModels.Stacks.StacksViewModel(
                    new MyGit.Core.Stacks.StackService(git, new MyGit.Core.Operations.RebaseService(git)),
                    new BranchService(git),
                    historyService,
                    new MyGit.GitHub.PullRequests.PullRequestService(accounts),
                    new MyGit.Core.PullRequests.PullRequestDraftService(git),
                    pullRequests,
                    branches,
                    dialogs,
                    shell));

            // Signing in or out changes which pull requests we can see.
            accounts.AccountChanged += () => Avalonia.Threading.Dispatcher.UIThread.Post(() => _ = viewModel.PullRequests.OnAccountChangedAsync());

            main = viewModel;
            viewModel.UseShell(shell);
            viewModel.UseUpdates(new UpdatesViewModel(accounts));
            viewModel.Operation.AutoStashByDefault = () => settingsStore.Settings.AutoStashByDefault;

            if (OperatingSystem.IsMacOS())
            {
                AddMacMenu(viewModel);
            }

            window.DataContext = viewModel;
            desktop.MainWindow = window;

            _ = viewModel.InitializeAsync();
            viewModel.Updates?.Start();
        }

        base.OnFrameworkInitializationCompleted();
    }

    // The Mac menu bar's "My Git" menu. macOS adds Hide, Hide Others and Quit (⌘Q) itself.
    private void AddMacMenu(MainWindowViewModel viewModel)
    {
        var menu = new NativeMenu();
        menu.Add(new NativeMenuItem("Settings…") { Command = viewModel.OpenSettingsCommand, Gesture = new KeyGesture(Key.OemComma, KeyModifiers.Meta) });
        menu.Add(new NativeMenuItem("Command Palette") { Command = viewModel.OpenPaletteCommand, Gesture = new KeyGesture(Key.P, KeyModifiers.Meta | KeyModifiers.Shift) });
        menu.Add(new NativeMenuItem("Keyboard Shortcuts") { Command = viewModel.ShowShortcutsCommand });
        if (viewModel.Updates is { } updates)
        {
            menu.Add(new NativeMenuItem("Check for Updates…") { Command = updates.CheckNowCommand });
        }

        NativeMenu.SetMenu(this, menu);
    }
}
