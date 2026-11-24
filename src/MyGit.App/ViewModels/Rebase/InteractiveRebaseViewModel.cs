using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyGit.Core.Operations;

namespace MyGit.App.ViewModels.Rebase;

/// <summary>One commit in the interactive rebase window: what happens to it, and its message.</summary>
public sealed partial class RebaseRowViewModel : ObservableObject
{
    public RebaseRowViewModel(RebaseCommit commit)
    {
        Commit = commit;
        Message = commit.Message;
    }

    /// <summary>The choices in the row's dropdown, in <see cref="RebaseAction"/> order.</summary>
    public static IReadOnlyList<string> ActionNames { get; } = ["pick", "reword", "edit", "squash", "fixup", "drop"];

    public RebaseCommit Commit { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActionIndex), nameof(IsMessageEditable), nameof(IsSubjectShown), nameof(IsDropped), nameof(IsJoined), nameof(IsReword), nameof(IsEdit), nameof(Hint))]
    public partial RebaseAction Action { get; set; } = RebaseAction.Pick;

    /// <summary>For the dropdown.</summary>
    public int ActionIndex
    {
        get => (int)Action;
        set
        {
            if (value >= 0)
            {
                Action = (RebaseAction)value;
            }
        }
    }

    /// <summary>Reword: the new message. Squash: this commit's part of the combined message.</summary>
    [ObservableProperty]
    public partial string Message { get; set; }

    public bool IsMessageEditable => Action is RebaseAction.Reword or RebaseAction.Squash;

    public bool IsSubjectShown => !IsMessageEditable;

    public bool IsDropped => Action == RebaseAction.Drop;

    public bool IsReword => Action == RebaseAction.Reword;

    public bool IsEdit => Action == RebaseAction.Edit;

    /// <summary>Squash and fixup join the commit above: shown indented under it.</summary>
    public bool IsJoined => Action is RebaseAction.Squash or RebaseAction.Fixup;

    public string Hint => Action switch
    {
        RebaseAction.Squash => "joins the commit above; its message is added below that one",
        RebaseAction.Fixup => "joins the commit above, keeping that message",
        RebaseAction.Edit => "stops here so you can change it",
        RebaseAction.Drop => "left out",
        _ => string.Empty,
    };

    public RebaseStep ToStep() => new(Commit, Action, IsMessageEditable ? Message : null);
}

/// <summary>
/// The interactive rebase window: the commits to replay (oldest first), reordered and
/// given an action each, plus auto-stash. <see cref="Started"/> is true when the user pressed Start.
/// </summary>
public sealed partial class InteractiveRebaseViewModel : ObservableObject
{
    /// <param name="description">What is rebased, e.g. "feature onto main" or "feature from a1b2c3d".</param>
    public InteractiveRebaseViewModel(string description, IReadOnlyList<RebaseCommit> commits, bool flattensMerges)
    {
        Description = description;
        FlattensMerges = flattensMerges;

        foreach (var commit in commits)
        {
            var row = new RebaseRowViewModel(commit);
            row.PropertyChanged += (_, _) => Validate();
            Rows.Add(row);
        }

        Rows.CollectionChanged += (_, _) => Validate();
        SelectedRow = Rows.FirstOrDefault();
        Validate();
    }

    /// <summary>Raised when the window should close (after Start or Cancel).</summary>
    public event Action? Finished;

    public string Description { get; }

    public string Title => $"{Description} · {(Rows.Count == 1 ? "1 commit" : $"{Rows.Count} commits")}";

    public bool FlattensMerges { get; }

    public ObservableCollection<RebaseRowViewModel> Rows { get; } = [];

    [ObservableProperty]
    public partial RebaseRowViewModel? SelectedRow { get; set; }

    [ObservableProperty]
    public partial bool AutoStash { get; set; } = true;

    /// <summary>Why the plan can't start yet (e.g. a squash with nothing above), or empty.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    public partial string ErrorText { get; set; } = string.Empty;

    public bool HasError => ErrorText.Length > 0;

    public bool Started { get; private set; }

    public IReadOnlyList<RebaseStep> Plan => Rows.Select(row => row.ToStep()).ToList();

    /// <summary>Moves a row to a new position (drag and drop, Alt+↑/↓).</summary>
    public void Move(RebaseRowViewModel row, int newIndex)
    {
        var oldIndex = Rows.IndexOf(row);
        newIndex = Math.Clamp(newIndex, 0, Rows.Count - 1);
        if (oldIndex < 0 || oldIndex == newIndex)
        {
            return;
        }

        Rows.Move(oldIndex, newIndex);
        SelectedRow = row;
    }

    /// <summary>Sets the selected row's action from its key (P, R, E, S, F, D). Returns false for other keys.</summary>
    public bool SetActionFromKey(char key)
    {
        RebaseAction? action = char.ToUpperInvariant(key) switch
        {
            'P' => RebaseAction.Pick,
            'R' => RebaseAction.Reword,
            'E' => RebaseAction.Edit,
            'S' => RebaseAction.Squash,
            'F' => RebaseAction.Fixup,
            'D' => RebaseAction.Drop,
            _ => null,
        };

        if (action is null || SelectedRow is null)
        {
            return false;
        }

        SelectedRow.Action = action.Value;
        return true;
    }

    [RelayCommand]
    private void MoveUp()
    {
        if (SelectedRow is { } row)
        {
            Move(row, Rows.IndexOf(row) - 1);
        }
    }

    [RelayCommand]
    private void MoveDown()
    {
        if (SelectedRow is { } row)
        {
            Move(row, Rows.IndexOf(row) + 1);
        }
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private void Start()
    {
        Started = true;
        Finished?.Invoke();
    }

    [RelayCommand]
    private void Cancel() => Finished?.Invoke();

    private bool CanStart() => !HasError;

    private void Validate()
    {
        var firstKept = Rows.FirstOrDefault(row => !row.IsDropped);
        if (firstKept is { IsJoined: true })
        {
            ErrorText = $"“{firstKept.Commit.Subject}” can't be a {RebaseRowViewModel.ActionNames[firstKept.ActionIndex]}: there's no commit above it to join.";
        }
        else if (Rows.Any(row => row.Action == RebaseAction.Reword && row.Message.Trim().Length == 0))
        {
            ErrorText = "A reworded commit needs a message.";
        }
        else
        {
            ErrorText = string.Empty;
        }
    }
}
