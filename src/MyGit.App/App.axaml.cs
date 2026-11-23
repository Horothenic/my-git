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
            var accounts = new AccountManager(new WindowsCredentialTokenStore());
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

            var themeService = new ThemeService(settingsStore);
            themeService.ApplySaved();

            var window = new MainWindow();
            var shell = new ShellService(window);

            var dialogs = new DialogService(
                window,
                createSignIn: () => new SignInViewModel(deviceFlow, accounts, shell),
                createClone: folderPicker => new CloneViewModel(
                    cloneService, gitHubRepositories, accounts, repositoryStore, repositoryService, settingsStore, folderPicker));

            var viewModel = new MainWindowViewModel(
                repositoryStore,
                repositoryService,
                settingsStore,
                themeService,
                new FolderPicker(window),
                dialogs,
                new AccountViewModel(accounts, dialogs, shell),
                new ChangesViewModel(changesService, new PartialChangesService(git, changesService, diffService), commitService, diffService, hiddenFiles, repositoryService, settingsStore, dialogs, shell),
                new SyncViewModel(new SyncService(git), dialogs));

            window.DataContext = viewModel;
            desktop.MainWindow = window;

            _ = viewModel.InitializeAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
