using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyGit.GitHub;
using MyGit.GitHub.Accounts;
using Velopack;
using Velopack.Sources;

namespace MyGit.App.ViewModels;

/// <summary>
/// My Git's own updates. The installed app checks My Git's GitHub releases at startup and every 6 hours
/// (with your account: the repository is private), downloads a newer version in the background, then offers
/// "Restart to update" in the bottom bar. Ignored, it's applied the next time My Git starts.
/// Builds from source ("dev build") never update themselves.
/// </summary>
public sealed partial class UpdatesViewModel : ObservableObject
{
    private static readonly TimeSpan CheckEvery = TimeSpan.FromHours(6);

    private readonly AccountManager _accounts;
    private readonly DispatcherTimer _timer;
    private UpdateManager? _manager;
    private UpdateInfo? _ready;

    public UpdatesViewModel(AccountManager accounts)
    {
        _accounts = accounts;
        _timer = new DispatcherTimer { Interval = CheckEvery };
        _timer.Tick += (_, _) => _ = CheckAsync(quiet: true);
    }

    /// <summary>A short note for the bottom bar, e.g. "You have the latest version (1.2.0)".</summary>
    public event Action<string>? NoticeRaised;

    public event Action<string>? MessageRaised;

    /// <summary>"1.2.0", or "dev build" when running from source.</summary>
    public string Version => Manager()?.CurrentVersion?.ToString() ?? "dev build";

    /// <summary>For the account menu: "Version 1.2.0".</summary>
    public string VersionText => Manager() is null ? "Dev build (built from source, no updates)" : $"Version {Version}";

    public bool IsInstalled => Manager() is not null;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckNowCommand))]
    public partial bool IsChecking { get; set; }

    /// <summary>A newer version is downloaded and waits for a restart.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReadyText))]
    public partial string? ReadyVersion { get; set; }

    public bool IsReady => ReadyVersion is not null;

    public string ReadyText => ReadyVersion is null ? string.Empty : $"My Git {ReadyVersion} is ready";

    partial void OnReadyVersionChanged(string? value) => OnPropertyChanged(nameof(IsReady));

    /// <summary>Starts checking: now, and every 6 hours. Call once at startup.</summary>
    public void Start()
    {
        if (Manager() is null)
        {
            return; // dev build
        }

        _timer.Start();
        _ = CheckAsync(quiet: true);
    }

    [RelayCommand(CanExecute = nameof(CanCheck))]
    private Task CheckNowAsync() => CheckAsync(quiet: false);

    /// <summary>Closes My Git, swaps in the new version, and starts it again.</summary>
    [RelayCommand]
    private void RestartToUpdate()
    {
        if (_manager is not null && _ready is not null)
        {
            _manager.ApplyUpdatesAndRestart(_ready.TargetFullRelease);
        }
    }

    private bool CanCheck() => !IsChecking;

    private async Task CheckAsync(bool quiet)
    {
        if (Manager() is not { } manager)
        {
            if (!quiet)
            {
                NoticeRaised?.Invoke("This is a dev build (built from source), so it doesn't update itself. Install My Git from a release to get updates.");
            }

            return;
        }

        if (IsChecking || IsReady)
        {
            return;
        }

        if (_accounts.AccessToken is null)
        {
            if (!quiet)
            {
                MessageRaised?.Invoke("Sign in to GitHub to check for updates: My Git's releases are in a private repository.");
            }

            return;
        }

        IsChecking = true;
        try
        {
            if (await manager.CheckForUpdatesAsync() is not { } update)
            {
                if (!quiet)
                {
                    NoticeRaised?.Invoke($"You have the latest version ({Version})");
                }

                return;
            }

            // Download in the background; it's verified against the release's checksum before it's used.
            await manager.DownloadUpdatesAsync(update);
            _ready = update;
            ReadyVersion = update.TargetFullRelease.Version.ToString();

            // Not restarted now? Then the next start gets it.
            manager.WaitExitThenApplyUpdates(update.TargetFullRelease, silent: true, restart: false);
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or IOException or TaskCanceledException)
        {
            if (!quiet)
            {
                MessageRaised?.Invoke($"Couldn't check for updates: {exception.Message}");
            }
        }
        finally
        {
            IsChecking = false;
        }
    }

    // Built anew when signing in or out, since the token is part of the source. A dev build is told once.
    private UpdateManager? Manager()
    {
        if (_isDevBuild)
        {
            return null;
        }

        var token = _accounts.AccessToken;
        if (_manager is null || _managerToken != token)
        {
            var manager = new UpdateManager(new GithubSource(GitHubAppInfo.ReleasesRepository, token, prerelease: false));
            _isDevBuild = !manager.IsInstalled;
            _manager = _isDevBuild ? null : manager;
            _managerToken = token;
        }

        return _manager;
    }

    private string? _managerToken;
    private bool _isDevBuild;
}
