using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyGit.Core.Changes;
using MyGit.Core.Diffs;
using MyGit.Core.Git;

namespace MyGit.App.ViewModels.Changes;

public enum DiffState
{
    /// <summary>Nothing selected.</summary>
    None,
    Loading,
    Lines,
    Binary,

    /// <summary>The file is big: wait for "Load diff anyway".</summary>
    TooLarge,

    /// <summary>Only file mode or line endings changed.</summary>
    NoTextChanges,
    Error,
}

/// <summary>
/// The right-hand diff panel for the selected file.
/// </summary>
public sealed partial class DiffViewModel : ObservableObject
{
    private readonly DiffService _diffService;
    private CancellationTokenSource? _loading;
    private string? _repositoryPath;

    public DiffViewModel(DiffService diffService)
    {
        _diffService = diffService;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNone), nameof(IsLoading), nameof(HasLines), nameof(IsBinary), nameof(IsTooLarge), nameof(IsNoTextChanges), nameof(IsError), nameof(HasFile))]
    public partial DiffState State { get; set; } = DiffState.None;

    public bool IsNone => State == DiffState.None;

    public bool IsLoading => State == DiffState.Loading;

    public bool HasLines => State == DiffState.Lines;

    public bool IsBinary => State == DiffState.Binary;

    public bool IsTooLarge => State == DiffState.TooLarge;

    public bool IsNoTextChanges => State == DiffState.NoTextChanges;

    public string NoTextChangesText => Change?.Kind switch
    {
        ChangeKind.Renamed => "Renamed. The content didn't change.",
        ChangeKind.Copied => "Copied. The content didn't change.",
        ChangeKind.Added or ChangeKind.Untracked => "Empty file.",
        _ => "Only file permissions or line endings changed.",
    };

    public bool IsError => State == DiffState.Error;

    public bool HasFile => State != DiffState.None;

    /// <summary>The file shown, or null.</summary>
    [ObservableProperty]
    public partial FileChange? Change { get; set; }

    /// <summary>True when the file comes from the Staged list.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUnstaged))]
    public partial bool IsStaged { get; set; }

    public bool IsUnstaged => !IsStaged;

    public string Title => Change is null ? string.Empty : Change.OriginalPath is { } original ? $"{original} → {Change.Path}" : Change.Path;

    [ObservableProperty]
    public partial IReadOnlyList<DiffRowViewModel> Rows { get; set; } = [];

    [ObservableProperty]
    public partial string? ErrorText { get; set; }

    /// <summary>Shows the diff of a file, or clears the panel when <paramref name="change"/> is null.</summary>
    public async Task ShowAsync(string repositoryPath, FileChange? change, bool staged, bool forceLarge = false)
    {
        _loading?.Cancel();
        _loading = new CancellationTokenSource();
        var token = _loading.Token;

        _repositoryPath = repositoryPath;
        Change = change;
        IsStaged = staged;
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(NoTextChangesText));

        if (change is null)
        {
            Rows = [];
            State = DiffState.None;
            return;
        }

        if (!forceLarge && change.Kind != ChangeKind.Deleted && DiffService.IsLarge(repositoryPath, change.Path))
        {
            Rows = [];
            State = DiffState.TooLarge;
            return;
        }

        // Only show "Loading" if it takes a moment, so quick switches don't flicker.
        var indicator = CancellationTokenSource.CreateLinkedTokenSource(token);
        _ = ShowLoadingAfterDelayAsync(indicator.Token);

        try
        {
            var diff = await _diffService.GetDiffAsync(repositoryPath, change, staged, token);
            indicator.Cancel();
            var rows = diff.IsBinary ? [] : SideBySide.Build(diff).Select(row => new DiffRowViewModel(row)).ToList();

            if (token.IsCancellationRequested)
            {
                return;
            }

            Rows = rows;
            State = diff.IsBinary ? DiffState.Binary : rows.Count == 0 ? DiffState.NoTextChanges : DiffState.Lines;
        }
        catch (OperationCanceledException)
        {
            // Another file was selected.
        }
        catch (Exception exception) when (exception is GitCommandException or IOException or UnauthorizedAccessException)
        {
            if (!token.IsCancellationRequested)
            {
                ErrorText = exception.Message;
                State = DiffState.Error;
            }
        }
        finally
        {
            indicator.Cancel();
            indicator.Dispose();
        }
    }

    private async Task ShowLoadingAfterDelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(150, cancellationToken);
            State = DiffState.Loading;
        }
        catch (OperationCanceledException)
        {
            // Loaded quickly (or another file was picked): no indicator needed.
        }
    }

    [RelayCommand]
    private Task LoadAnywayAsync() =>
        _repositoryPath is null ? Task.CompletedTask : ShowAsync(_repositoryPath, Change, IsStaged, forceLarge: true);
}
