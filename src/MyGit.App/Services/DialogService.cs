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

    /// <param name="createClone">Builds the clone view model; gets a folder picker owned by the clone window.</param>
    public DialogService(Window owner, Func<SignInViewModel> createSignIn, Func<IFolderPicker, CloneViewModel> createClone)
    {
        _owner = owner;
        _createSignIn = createSignIn;
        _createClone = createClone;
    }

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
