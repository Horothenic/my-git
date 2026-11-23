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

/// <summary>What the user asked to do with hunks or picked lines.</summary>
public enum DiffAction
{
    /// <summary>Stage (in an Unstaged diff) or unstage (in a Staged diff).</summary>
    ToggleStage,

    /// <summary>Throw the change away (Unstaged diffs only).</summary>
    Discard,
}

/// <summary>
/// The side-by-side diff panel. Shown by the Changes tab (editable) and the History tab (read-only).
/// </summary>
public sealed partial class DiffViewModel : ObservableObject
{
    private readonly DiffService _diffService;
    private readonly HashSet<DiffLineRef> _selection = [];
    private CancellationTokenSource? _loading;
    private string? _repositoryPath;

    // Where a Shift+click or drag range starts.
    private (DiffRowViewModel Row, DiffSide Side)? _anchor;

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

    /// <summary>
    /// Raised for hunk and line actions; the owner (the Changes tab) applies them with git.
    /// Arguments: what to do, and which lines.
    /// </summary>
    public event Func<DiffAction, IReadOnlySet<DiffLineRef>, Task>? ActionRequested;

    /// <summary>Text when nothing is selected, e.g. "Select a file to see its changes."</summary>
    public string EmptyText { get; init; } = "Select a file to see its changes.";

    /// <summary>True for diffs that can't be changed (a commit in the History tab).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditLines), nameof(ShowUnstagedActions), nameof(ShowStagedActions))]
    public partial bool IsReadOnly { get; set; }

    /// <summary>Show "Discard file" / "Stage file" (an Unstaged file in the Changes tab).</summary>
    public bool ShowUnstagedActions => !IsReadOnly && IsUnstaged;

    /// <summary>Show "Unstage file" (a Staged file in the Changes tab).</summary>
    public bool ShowStagedActions => !IsReadOnly && IsStaged;

    /// <summary>The file shown, or null.</summary>
    [ObservableProperty]
    public partial FileChange? Change { get; set; }

    /// <summary>True when the file comes from the Staged list.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUnstaged), nameof(ShowUnstagedActions), nameof(ShowStagedActions))]
    public partial bool IsStaged { get; set; }

    public bool IsUnstaged => !IsStaged;

    public string Title => Change is null ? string.Empty : Change.OriginalPath is { } original ? $"{original} → {Change.Path}" : Change.Path;

    [ObservableProperty]
    public partial IReadOnlyList<DiffRowViewModel> Rows { get; set; } = [];

    [ObservableProperty]
    public partial string? ErrorText { get; set; }

    /// <summary>The diff behind <see cref="Rows"/>; line actions build their patch from it.</summary>
    public FileDiff? CurrentDiff { get; private set; }

    /// <summary>The picked lines.</summary>
    public IReadOnlySet<DiffLineRef> Selection => _selection;

    public int SelectedCount => _selection.Count;

    public bool HasSelection => _selection.Count > 0;

    public string SelectionText => SelectedCount == 1 ? "1 line selected" : $"{SelectedCount} lines selected";

    /// <summary>Hunk and line buttons only make sense for plain text changes (not renames, conflicts...).</summary>
    public bool CanEditLines => !IsReadOnly && State == DiffState.Lines && Change is { } change && PartialChangesService.Supports(change);

    /// <summary>Click on a changed line. Ctrl adds or removes it, Shift selects the range from the last click.</summary>
    public void Pick(DiffRowViewModel row, DiffSide side, bool ctrl, bool shift)
    {
        if (row.RefFor(side) is not { } picked || !CanEditLines)
        {
            return;
        }

        if (shift && _anchor is { } anchor && anchor.Side == side)
        {
            if (!ctrl)
            {
                ClearSelection(keepAnchor: true);
            }

            SelectRange(anchor.Row, row, side);
        }
        else if (ctrl)
        {
            SetPicked(row, side, picked, !_selection.Contains(picked));
            _anchor = (row, side);
        }
        else
        {
            ClearSelection(keepAnchor: false);
            SetPicked(row, side, picked, true);
            _anchor = (row, side);
        }

        NotifySelectionChanged();
    }

    /// <summary>Dragging over lines: select from where the drag started to <paramref name="row"/>.</summary>
    public void DragTo(DiffRowViewModel row, DiffSide side)
    {
        if (_anchor is not { } anchor || anchor.Side != side || row.RefFor(side) is null)
        {
            return;
        }

        ClearSelection(keepAnchor: true);
        SelectRange(anchor.Row, row, side);
        NotifySelectionChanged();
    }

    public void ClearSelection() => ClearSelection(keepAnchor: false);

    [RelayCommand]
    private Task ToggleStageHunkAsync(int hunkIndex) => RequestAsync(DiffAction.ToggleStage, HunkLines(hunkIndex));

    [RelayCommand]
    private Task DiscardHunkAsync(int hunkIndex) => RequestAsync(DiffAction.Discard, HunkLines(hunkIndex));

    [RelayCommand]
    private Task ToggleStageLinesAsync() => RequestAsync(DiffAction.ToggleStage, Selection.ToHashSet());

    [RelayCommand]
    private Task DiscardLinesAsync() => RequestAsync(DiffAction.Discard, Selection.ToHashSet());

    private IReadOnlySet<DiffLineRef> HunkLines(int hunkIndex) =>
        CurrentDiff is { } diff ? PatchBuilder.AllChanges(diff, hunkIndex) : new HashSet<DiffLineRef>();

    private Task RequestAsync(DiffAction action, IReadOnlySet<DiffLineRef> lines) =>
        CanEditLines && lines.Count > 0 && ActionRequested is { } handler ? handler(action, lines) : Task.CompletedTask;

    /// <summary>The bar's Clear button and Esc.</summary>
    [RelayCommand]
    private void ClearPicked() => ClearSelection(keepAnchor: false);

    private void ClearSelection(bool keepAnchor)
    {
        foreach (var row in Rows)
        {
            row.IsLeftSelected = false;
            row.IsRightSelected = false;
        }

        _selection.Clear();
        if (!keepAnchor)
        {
            _anchor = null;
        }

        NotifySelectionChanged();
    }

    private void SelectRange(DiffRowViewModel from, DiffRowViewModel to, DiffSide side)
    {
        var start = IndexOf(from);
        var end = IndexOf(to);
        if (start < 0 || end < 0)
        {
            return;
        }

        for (var i = Math.Min(start, end); i <= Math.Max(start, end); i++)
        {
            if (Rows[i].RefFor(side) is { } picked)
            {
                SetPicked(Rows[i], side, picked, true);
            }
        }
    }

    private int IndexOf(DiffRowViewModel row)
    {
        for (var i = 0; i < Rows.Count; i++)
        {
            if (ReferenceEquals(Rows[i], row))
            {
                return i;
            }
        }

        return -1;
    }

    private void SetPicked(DiffRowViewModel row, DiffSide side, DiffLineRef picked, bool selected)
    {
        row.SetSelected(side, selected);

        if (selected)
        {
            _selection.Add(picked);
        }
        else
        {
            _selection.Remove(picked);
        }
    }

    private void NotifySelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectionText));
    }

    /// <summary>Shows a file of a commit (read-only), or clears the panel when <paramref name="file"/> is null.</summary>
    public Task ShowCommitFileAsync(string repositoryPath, FileChange? file, Func<CancellationToken, Task<FileDiff>> load)
    {
        IsReadOnly = true;
        return LoadAsync(repositoryPath, file, staged: false, forceLarge: true, load);
    }

    /// <summary>Shows the diff of a file, or clears the panel when <paramref name="change"/> is null.</summary>
    public Task ShowAsync(string repositoryPath, FileChange? change, bool staged, bool forceLarge = false)
    {
        IsReadOnly = false;
        return LoadAsync(repositoryPath, change, staged, forceLarge,
            token => _diffService.GetDiffAsync(repositoryPath, change!, staged, token));
    }

    private Func<CancellationToken, Task<FileDiff>>? _lastLoad;

    private async Task LoadAsync(string repositoryPath, FileChange? change, bool staged, bool forceLarge, Func<CancellationToken, Task<FileDiff>> load)
    {
        _lastLoad = load;
        _loading?.Cancel();
        _loading = new CancellationTokenSource();
        var token = _loading.Token;

        // A refresh of the same file: keep the rows (and the picked lines) unless the diff changed.
        var isReload = change is not null && Change is not null && State == DiffState.Lines &&
                       change.Path == Change.Path && staged == IsStaged;

        _repositoryPath = repositoryPath;
        Change = change;
        IsStaged = staged;
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(NoTextChangesText));

        if (change is null)
        {
            SetRows(null, []);
            State = DiffState.None;
            return;
        }

        if (!forceLarge && change.Kind != ChangeKind.Deleted && DiffService.IsLarge(repositoryPath, change.Path))
        {
            SetRows(null, []);
            State = DiffState.TooLarge;
            return;
        }

        // Only show "Loading" if it takes a moment, so quick switches don't flicker.
        var indicator = CancellationTokenSource.CreateLinkedTokenSource(token);
        if (!isReload)
        {
            _ = ShowLoadingAfterDelayAsync(indicator.Token);
        }

        try
        {
            var diff = await load(token);
            indicator.Cancel();
            if (token.IsCancellationRequested || (isReload && diff.RawText == CurrentDiff?.RawText))
            {
                return;
            }

            var rows = diff.IsBinary ? [] : SideBySide.Build(diff).Select(row => new DiffRowViewModel(row)).ToList();
            SetRows(diff, rows);
            State = diff.IsBinary ? DiffState.Binary : rows.Count == 0 ? DiffState.NoTextChanges : DiffState.Lines;
            OnPropertyChanged(nameof(CanEditLines));
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

    private void SetRows(FileDiff? diff, IReadOnlyList<DiffRowViewModel> rows)
    {
        CurrentDiff = diff;
        Rows = rows;
        _selection.Clear();
        _anchor = null;
        NotifySelectionChanged();
        OnPropertyChanged(nameof(CanEditLines));
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
        _repositoryPath is null || _lastLoad is null ? Task.CompletedTask : LoadAsync(_repositoryPath, Change, IsStaged, forceLarge: true, _lastLoad);
}
