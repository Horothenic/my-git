using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyGit.App.Services;
using MyGit.GitHub.Accounts;

namespace MyGit.App.ViewModels;

/// <summary>
/// The account button at the top right and its menu.
/// </summary>
public sealed partial class AccountViewModel : ObservableObject
{
    private readonly AccountManager _accounts;
    private readonly IDialogService _dialogs;
    private readonly IShellService _shell;

    public AccountViewModel(AccountManager accounts, IDialogService dialogs, IShellService shell)
    {
        _accounts = accounts;
        _dialogs = dialogs;
        _shell = shell;

        _accounts.AccountChanged += () => _ = RefreshAsync();
    }

    public bool IsSignedIn => _accounts.Account is not null;

    public bool IsSignedOut => !IsSignedIn;

    public string DisplayName => _accounts.Account?.DisplayName ?? "Not signed in";

    public string Login => _accounts.Account?.Login ?? string.Empty;

    /// <summary>First letter of the login, shown until the avatar has downloaded.</summary>
    public string Initial => Login.Length > 0 ? Login[..1].ToUpperInvariant() : "?";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAvatar))]
    public partial Bitmap? Avatar { get; set; }

    public bool HasAvatar => Avatar is not null;

    /// <summary>Loads the saved account. Call once at startup.</summary>
    public Task InitializeAsync() => _accounts.RestoreAsync();

    /// <summary>Raised so the view can close the account menu.</summary>
    public event Action? DialogOpening;

    [RelayCommand]
    private Task SignInAsync()
    {
        DialogOpening?.Invoke();
        return _dialogs.ShowSignInAsync();
    }

    [RelayCommand]
    private void SignOut()
    {
        DialogOpening?.Invoke();
        _accounts.SignOut();
    }

    [RelayCommand]
    private void ViewProfile()
    {
        if (_accounts.Account is { } account)
        {
            _shell.OpenUrl(account.ProfileUrl);
        }
    }

    private async Task RefreshAsync()
    {
        OnPropertyChanged(nameof(IsSignedIn));
        OnPropertyChanged(nameof(IsSignedOut));
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(Login));
        OnPropertyChanged(nameof(Initial));

        Avatar = _accounts.Account is { } account ? await AvatarLoader.LoadAsync(account.AvatarUrl) : null;
    }
}
