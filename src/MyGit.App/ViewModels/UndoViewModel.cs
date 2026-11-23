using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyGit.App.Services;
using MyGit.Core.Git;
using MyGit.Core.Undo;

namespace MyGit.App.ViewModels;

/// <summary>One "go back to before …" entry in the Undo menu.</summary>
public sealed partial class UndoPointItemViewModel(UndoPoint point, UndoViewModel owner)
{
    public UndoPoint Point { get; } = point;

    public string Text => $"Before {Point.Description}  ·  {UndoViewModel.Ago(Point.HappenedAt)}";

    [RelayCommand]
    private Task UndoAsync() => owner.UndoToAsync(Point);
}

/// <summary>
/// The toolbar Undo button: one click undoes the last thing that moved the branch; right-click
/// lists earlier points. Uncommitted changes are always kept.
/// </summary>
public sealed partial class UndoViewModel : ObservableObject
{
    private readonly UndoService _undo;
    private readonly IDialogService _dialogs;
    private string? _repositoryPath;

    public UndoViewModel(UndoService undo, IDialogService dialogs)
    {
        _undo = undo;
        _dialogs = dialogs;
    }

    /// <summary>Raised with an error for the message bar.</summary>
    public event Action<string>? MessageRaised;

    /// <summary>Raised with a short success note.</summary>
    public event Action<string>? NoticeRaised;

    /// <summary>Raised after the branch moved, so the window refreshes.</summary>
    public event Func<Task>? RepositoryChanged;

    public ObservableCollection<UndoPointItemViewModel> Points { get; } = [];

    public bool CanUndo => Points.Count > 0;

    public string ToolTip => Points.Count == 0
        ? "Nothing to undo on this branch"
        : $"Undo {Points[0].Point.Description} ({Ago(Points[0].Point.HappenedAt)}). Right-click for earlier points. Uncommitted changes are kept.";

    public async Task RefreshAsync(string? repositoryPath)
    {
        _repositoryPath = repositoryPath;
        Points.Clear();

        if (repositoryPath is not null)
        {
            try
            {
                foreach (var point in await _undo.GetPointsAsync(repositoryPath))
                {
                    Points.Add(new UndoPointItemViewModel(point, this));
                }
            }
            catch (GitCommandException)
            {
                // No reflog (e.g. a brand-new repository): nothing to undo.
            }
        }

        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(ToolTip));
        UndoLastCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private Task UndoLastAsync() => Points.Count > 0 ? UndoToAsync(Points[0].Point) : Task.CompletedTask;

    public async Task UndoToAsync(UndoPoint point)
    {
        if (_repositoryPath is not { } repository)
        {
            return;
        }

        try
        {
            // Moving a pushed commit away here means the next push has to be a force push.
            if (await _undo.IsCurrentCommitPushedAsync(repository) &&
                !await _dialogs.ConfirmAsync(
                    $"Undo {point.Description}?",
                    "What you'd undo is already on GitHub. Undoing it here means your next push has to be a force push. " +
                    "To take back a pushed commit safely, use Revert in the History tab instead.",
                    "Undo anyway"))
            {
                return;
            }

            if (await _undo.UndoAsync(repository, point) == UndoOutcome.WouldLoseChanges)
            {
                MessageRaised?.Invoke("Can't undo that without losing your uncommitted changes to the same files. Commit or stash them first. Nothing was changed.");
                return;
            }

            NoticeRaised?.Invoke($"Undid {point.Description}. Undo again to redo it.");
        }
        catch (GitCommandException exception)
        {
            MessageRaised?.Invoke(exception.Message);
        }

        if (RepositoryChanged is { } handler)
        {
            await handler();
        }
    }

    public static string Ago(DateTimeOffset when)
    {
        var age = DateTimeOffset.Now - when;
        return age switch
        {
            { TotalMinutes: < 1 } => "just now",
            { TotalMinutes: < 60 } => $"{(int)age.TotalMinutes} min ago",
            { TotalHours: < 24 } => $"{(int)age.TotalHours} h ago",
            _ => $"{(int)age.TotalDays} days ago",
        };
    }
}
