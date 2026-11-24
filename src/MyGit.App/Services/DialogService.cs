using Avalonia.Controls;
using MyGit.App.ViewModels;
using MyGit.App.Views;

namespace MyGit.App.Services;

/// <summary>
/// <see cref="IDialogService"/> that opens modal windows on top of the main window.
/// </summary>
public sealed class DialogService : IDialogService
{
    private readonly Window _owner;
    private readonly Func<SignInViewModel> _createSignIn;
    private readonly Func<IFolderPicker, CloneViewModel> _createClone;
    private readonly Func<IFolderPicker, SettingsViewModel> _createSettings;

    /// <param name="createClone">Builds the clone view model; gets a folder picker owned by the clone window.</param>
    /// <param name="createSettings">Builds the Settings view model; gets a folder picker owned by the Settings window.</param>
    public DialogService(
        Window owner,
        Func<SignInViewModel> createSignIn,
        Func<IFolderPicker, CloneViewModel> createClone,
        Func<IFolderPicker, SettingsViewModel> createSettings)
    {
        _owner = owner;
        _createSignIn = createSignIn;
        _createClone = createClone;
        _createSettings = createSettings;
    }

    public async Task ShowSettingsAsync()
    {
        var window = new SettingsWindow();
        window.DataContext = _createSettings(new FolderPicker(window));
        await window.ShowDialog(_owner);
    }

    public Task ShowShortcutsAsync() => new ShortcutsWindow().ShowDialog(_owner);

    public Task<bool> ShowSignInAsync() => ShowSignInAsync(_owner);

    public async Task<string?> ShowCloneAsync()
    {
        var window = new CloneWindow();
        var viewModel = _createClone(new FolderPicker(window));
        window.DataContext = viewModel;
        string? clonedPath = null;

        viewModel.Finished += path =>
        {
            clonedPath = path;
            window.Close();
        };

        viewModel.SignInRequested += async () =>
        {
            if (await ShowSignInAsync(window))
            {
                await viewModel.OnSignedInAsync();
            }
        };

        window.Opened += (_, _) => _ = viewModel.LoadAsync();

        // Closing with the window's X while a clone runs cancels it too.
        // (StopCloning, not CancelCommand: that would ask the closing window to close again.)
        window.Closing += (_, _) => viewModel.StopCloning();

        await window.ShowDialog(_owner);
        return clonedPath;
    }

    public async Task<bool> ConfirmAsync(string title, string message, string confirmText)
    {
        var window = new ConfirmWindow(title, message, confirmText);
        await window.ShowDialog(_owner);
        return window.Confirmed;
    }

    public async Task<(bool Confirmed, bool Option)> ConfirmWithOptionAsync(string title, string message, string confirmText, string optionText, bool optionDefault, bool dangerous)
    {
        var window = new ConfirmWindow(title, message, confirmText, optionText, optionDefault, dangerous);
        await window.ShowDialog(_owner);
        return (window.Confirmed, window.IsOptionChecked);
    }

    public async Task<int> ChooseAsync(string title, string message, IReadOnlyList<string> choices, int dangerIndex = -1)
    {
        var window = new ChoiceWindow(title, message, choices, dangerIndex);
        await window.ShowDialog(_owner);
        return window.Choice;
    }

    public async Task<NameDialogResult?> AskNameAsync(NameDialogOptions options)
    {
        var window = new NameWindow(options);
        await window.ShowDialog(_owner);
        return window.Result;
    }

    public async Task<MyGit.Core.History.ResetMode?> AskResetAsync(string branch, string shortSha, int commitsUndone)
    {
        var window = new ResetWindow(branch, shortSha, commitsUndone);
        await window.ShowDialog(_owner);
        return window.Result;
    }

    public async Task<bool> ShowInteractiveRebaseAsync(ViewModels.Rebase.InteractiveRebaseViewModel viewModel)
    {
        var window = new InteractiveRebaseWindow { DataContext = viewModel };
        await window.ShowDialog(_owner);
        return viewModel.Started;
    }

    public async Task<bool> ShowCreatePullRequestAsync(ViewModels.PullRequests.CreatePullRequestViewModel viewModel)
    {
        var window = new CreatePullRequestWindow { DataContext = viewModel };
        await window.ShowDialog(_owner);
        return viewModel.Created is not null;
    }

    public async Task<bool> ShowNewStackAsync(ViewModels.Stacks.NewStackViewModel viewModel)
    {
        var window = new NewStackWindow { DataContext = viewModel };
        await window.ShowDialog(_owner);
        return viewModel.Result is not null;
    }

    private async Task<bool> ShowSignInAsync(Window owner)
    {
        var viewModel = _createSignIn();
        var window = new SignInWindow { DataContext = viewModel };
        var signedIn = false;

        viewModel.Finished += success =>
        {
            signedIn = success;
            window.Close();
        };

        window.Opened += (_, _) => _ = viewModel.StartAsync();

        // Closing with the window's X stops waiting for GitHub.
        // (StopWaiting, not CancelCommand: that would ask the closing window to close again.)
        window.Closing += (_, _) => viewModel.StopWaiting();

        await window.ShowDialog(owner);
        return signedIn;
    }
}
