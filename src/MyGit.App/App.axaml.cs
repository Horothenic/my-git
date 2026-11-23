using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MyGit.App.Services;
using MyGit.App.ViewModels;
using MyGit.App.Views;
using MyGit.Core.Git;
using MyGit.Core.Repositories;
using MyGit.Core.Settings;

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
            var git = new GitRunner();
            var settingsStore = new AppSettingsStore(AppSettingsStore.DefaultFilePath);
            var repositoryStore = new RepositoryStore(RepositoryStore.DefaultFilePath);
            var repositoryService = new RepositoryService(git);

            var themeService = new ThemeService(settingsStore);
            themeService.ApplySaved();

            var window = new MainWindow();
            var viewModel = new MainWindowViewModel(
                repositoryStore,
                repositoryService,
                settingsStore,
                themeService,
                new FolderPicker(window));

            window.DataContext = viewModel;
            desktop.MainWindow = window;

            _ = viewModel.InitializeAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
