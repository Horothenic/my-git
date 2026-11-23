using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyGit.App.Services;
using MyGit.GitHub.Accounts;
using Octokit;

namespace MyGit.App.ViewModels;

/// <summary>
/// The sign-in dialog: shows the device code, opens the browser and waits for approval.
/// </summary>
public sealed partial class SignInViewModel : ObservableObject
{
    private readonly DeviceFlowSignIn _signIn;
    private readonly AccountManager _accounts;
    private readonly IShellService _shell;
    private readonly CancellationTokenSource _cancellation = new();

    private DeviceCode? _code;
    private bool _waitingForApproval;

    public SignInViewModel(DeviceFlowSignIn signIn, AccountManager accounts, IShellService shell)
    {
        _signIn = signIn;
        _accounts = accounts;
        _shell = shell;
    }

    /// <summary>Raised when the dialog should close; true if the user is now signed in.</summary>
    public event Action<bool>? Finished;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCode))]
    public partial string UserCode { get; set; } = string.Empty;

    public bool HasCode => UserCode.Length > 0;

    [ObservableProperty]
    public partial string VerificationUrl { get; set; } = "https://github.com/login/device";

    [ObservableProperty]
    public partial string Status { get; set; } = "Contacting GitHub…";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; set; }

    public bool HasError => Error is not null;

    /// <summary>Call when the dialog opens.</summary>
    public async Task StartAsync()
    {
        try
        {
            _code = await _signIn.StartAsync(_cancellation.Token);
            UserCode = _code.UserCode;
            VerificationUrl = _code.VerificationUrl;

            // Save a step: the code is already on the clipboard when the browser opens.
            await _shell.CopyTextAsync(UserCode);
            _shell.OpenUrl(VerificationUrl);

            _waitingForApproval = true;
            _ = UpdateCountdownAsync(_code.ExpiresAt);

            var token = await _signIn.WaitForTokenAsync(_code, _cancellation.Token);
            _waitingForApproval = false;

            Status = "Signing in…";
            await _accounts.SignInAsync(token, _cancellation.Token);
            Finished?.Invoke(true);
        }
        catch (OperationCanceledException)
        {
            // The user closed the dialog.
        }
        catch (Exception exception) when (exception is ApiException or InvalidOperationException or HttpRequestException)
        {
            _waitingForApproval = false;
            Error = DescribeError(exception);
        }
    }

    [RelayCommand]
    private Task CopyCodeAsync() => _shell.CopyTextAsync(UserCode);

    [RelayCommand]
    private void OpenBrowser() => _shell.OpenUrl(VerificationUrl);

    [RelayCommand]
    private void Cancel()
    {
        StopWaiting();
        Finished?.Invoke(false);
    }

    /// <summary>Stops waiting for GitHub without asking the dialog to close (it's already closing).</summary>
    public void StopWaiting() => _cancellation.Cancel();

    private async Task UpdateCountdownAsync(DateTimeOffset expiresAt)
    {
        while (_waitingForApproval && !_cancellation.IsCancellationRequested)
        {
            var left = expiresAt - DateTimeOffset.Now;

            if (left <= TimeSpan.Zero)
            {
                Error = "The code expired. Close this window and sign in again.";
                return;
            }

            Status = $"Waiting for you to approve… code expires in {left:mm\\:ss}";

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(1), _cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private static string DescribeError(Exception exception) => exception switch
    {
        ApiException { Message: var message } when message.Contains("access_denied", StringComparison.OrdinalIgnoreCase)
            => "Sign-in was denied on GitHub.",
        ApiException { Message: var message } when message.Contains("expired", StringComparison.OrdinalIgnoreCase)
            => "The code expired. Close this window and sign in again.",
        HttpRequestException => "Couldn't reach GitHub. Check your connection and try again.",
        _ => exception.Message,
    };
}
