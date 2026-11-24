using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyGit.Core.Operations;

namespace MyGit.App.ViewModels.Changes;

/// <summary>A piece of the conflicted file as the resolver shows it: plain text, or one conflict.</summary>
public abstract class ResolverSegmentViewModel : ObservableObject;

/// <summary>Lines both sides agree on.</summary>
public sealed class CommonTextViewModel(string text) : ResolverSegmentViewModel
{
    public string Text { get; } = text;
}

/// <summary>
/// One conflict, VS Code style: the current change (green) and the incoming change (blue),
/// with Accept current / Accept incoming / Accept both above it, or what was taken and Undo.
/// </summary>
public sealed partial class ConflictBlockViewModel : ResolverSegmentViewModel
{
    private readonly Action<ConflictBlockViewModel> _chosen;

    public ConflictBlockViewModel(ConflictBlock block, int index, string currentLabel, string incomingLabel, Action<ConflictBlockViewModel> chosen)
    {
        Block = block;
        Index = index;
        CurrentHeader = $"<<<<<<<  Current change · {currentLabel}";
        IncomingHeader = $">>>>>>>  Incoming change · {incomingLabel}";
        _chosen = chosen;
    }

    public ConflictBlock Block { get; }

    public int Index { get; }

    public string CurrentHeader { get; }

    public string IncomingHeader { get; }

    public string CurrentText => Lines(Block.Ours, "(nothing here: this side removed these lines)");

    public string IncomingText => Lines(Block.Theirs, "(nothing here: this side removed these lines)");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsResolved), nameof(IsUnresolved), nameof(ResolvedText), nameof(ResolvedNote))]
    public partial ConflictChoice Choice { get; set; } = ConflictChoice.Unresolved;

    public bool IsResolved => Choice != ConflictChoice.Unresolved;

    public bool IsUnresolved => !IsResolved;

    /// <summary>The conflict the ↑/↓ buttons are on (shown with an accent edge).</summary>
    [ObservableProperty]
    public partial bool IsCurrent { get; set; }

    public string ResolvedNote => Choice switch
    {
        ConflictChoice.Ours => "✓ Took the current change",
        ConflictChoice.Theirs => "✓ Took the incoming change",
        ConflictChoice.Both => "✓ Took both (current first)",
        _ => string.Empty,
    };

    public string ResolvedText => Choice switch
    {
        ConflictChoice.Ours => Lines(Block.Ours, "(nothing kept here)"),
        ConflictChoice.Theirs => Lines(Block.Theirs, "(nothing kept here)"),
        ConflictChoice.Both => Lines([.. Block.Ours, .. Block.Theirs], "(nothing kept here)"),
        _ => string.Empty,
    };

    [RelayCommand]
    private void AcceptCurrent() => Choose(ConflictChoice.Ours);

    [RelayCommand]
    private void AcceptIncoming() => Choose(ConflictChoice.Theirs);

    [RelayCommand]
    private void AcceptBoth() => Choose(ConflictChoice.Both);

    [RelayCommand]
    private void Undo() => Choose(ConflictChoice.Unresolved);

    private void Choose(ConflictChoice choice)
    {
        Choice = choice;
        _chosen(this);
    }

    private static string Lines(IReadOnlyList<string> lines, string whenEmpty) => lines.Count == 0 ? whenEmpty : string.Join('\n', lines);
}

/// <summary>
/// The built-in resolver for one conflicted text file. The file is shown with each conflict in place
/// (current change green, incoming change blue) and decided one by one; or edited by hand as plain text.
/// </summary>
public sealed partial class ConflictResolverViewModel : ObservableObject
{
    private ConflictFile? _file;
    private Func<string, Task>? _save;
    private string _currentLabel = "current";
    private string _incomingLabel = "incoming";

    /// <summary>Raised when the view should scroll to the current conflict.</summary>
    public event Action<ConflictBlockViewModel>? ScrollRequested;

    /// <summary>True when a conflicted text file is loaded (otherwise the plain helpers are shown).</summary>
    [ObservableProperty]
    public partial bool IsActive { get; set; }

    /// <summary>The file, top to bottom: plain text and conflicts.</summary>
    public ObservableCollection<ResolverSegmentViewModel> Segments { get; } = [];

    public List<ConflictBlockViewModel> Blocks { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PositionText))]
    [NotifyCanExecuteChangedFor(nameof(PreviousCommand), nameof(NextCommand))]
    public partial int CurrentIndex { get; set; }

    public string PositionText => Blocks.Count == 0 ? string.Empty :
        $"Conflict {CurrentIndex + 1} of {Blocks.Count}" + (UnresolvedCount == 0 ? " · all decided" : $" · {UnresolvedCount} left");

    public int UnresolvedCount => Blocks.Count(block => block.IsUnresolved);

    /// <summary>e.g. "Current: main (rebasing onto)".</summary>
    [ObservableProperty]
    public partial string CurrentLegend { get; set; } = string.Empty;

    /// <summary>e.g. "Incoming: yours, “Add slot limit”".</summary>
    [ObservableProperty]
    public partial string IncomingLegend { get; set; } = string.Empty;

    /// <summary>Plain-text editing instead of the conflict view.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConflictView))]
    public partial bool IsEditingByHand { get; set; }

    public bool IsConflictView => !IsEditingByHand;

    /// <summary>The whole file while editing by hand.</summary>
    [ObservableProperty]
    public partial string HandText { get; set; } = string.Empty;

    /// <summary>Under the hand editor: what "Back to conflicts" will do.</summary>
    [ObservableProperty]
    public partial string HandNote { get; set; } = string.Empty;

    /// <param name="currentLabel">Who the current change ("ours") is from, e.g. "main (rebasing onto)".</param>
    /// <param name="incomingLabel">Who the incoming change ("theirs") is from, e.g. "yours, “Add slot limit”".</param>
    /// <param name="save">Writes the result to the file and marks it resolved (done by the Changes tab).</param>
    public void Load(ConflictFile file, string currentLabel, string incomingLabel, Func<string, Task> save)
    {
        _save = save;
        _currentLabel = currentLabel;
        _incomingLabel = incomingLabel;
        CurrentLegend = $"Current: {currentLabel}";
        IncomingLegend = $"Incoming: {incomingLabel}";
        IsEditingByHand = false;
        Show(file);
        IsActive = true;
    }

    public void Clear()
    {
        _file = null;
        Segments.Clear();
        Blocks.Clear();
        IsEditingByHand = false;
        IsActive = false;
    }

    /// <summary>The file as it will be saved.</summary>
    public string Result => IsEditingByHand || _file is null ? HandText : _file.Build(Blocks.Select(block => block.Choice).ToList());

    [RelayCommand(CanExecute = nameof(CanGoPrevious))]
    private void Previous() => GoTo(CurrentIndex - 1);

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private void Next() => GoTo(CurrentIndex + 1);

    [RelayCommand]
    private void EditByHand()
    {
        HandText = Result;
        HandNote = "Back to conflicts shows any conflict markers still in the text as conflicts again.";
        IsEditingByHand = true;
    }

    [RelayCommand]
    private void BackToConflicts()
    {
        if (ConflictFile.Parse(HandText) is not { } file)
        {
            HandNote = "No conflict markers are left in the text: press Save to mark the file resolved.";
            return;
        }

        IsEditingByHand = false;
        Show(file);
    }

    /// <summary>Saves the result to the file; the Changes tab marks it resolved if no markers are left.</summary>
    [RelayCommand]
    private Task SaveAsync() => _save?.Invoke(Result) ?? Task.CompletedTask;

    private bool CanGoPrevious() => CurrentIndex > 0;

    private bool CanGoNext() => CurrentIndex < Blocks.Count - 1;

    private void Show(ConflictFile file)
    {
        _file = file;
        Segments.Clear();
        Blocks.Clear();

        foreach (var segment in file.Segments)
        {
            if (segment is CommonSegment common)
            {
                Segments.Add(new CommonTextViewModel(string.Join('\n', common.Lines)));
                continue;
            }

            var block = new ConflictBlockViewModel((ConflictBlock)segment, Blocks.Count, _currentLabel, _incomingLabel, OnChosen);
            Blocks.Add(block);
            Segments.Add(block);
        }

        CurrentIndex = 0;
        MarkCurrent();
        OnPropertyChanged(nameof(UnresolvedCount));
        OnPropertyChanged(nameof(PositionText));
    }

    private void OnChosen(ConflictBlockViewModel block)
    {
        OnPropertyChanged(nameof(UnresolvedCount));
        OnPropertyChanged(nameof(PositionText));

        // After a choice, move on to the next undecided conflict (if any).
        if (block.IsResolved)
        {
            var next = Blocks.Skip(block.Index + 1).Concat(Blocks.Take(block.Index)).FirstOrDefault(other => other.IsUnresolved);
            GoTo(next?.Index ?? block.Index);
        }
        else
        {
            GoTo(block.Index);
        }
    }

    private void GoTo(int index)
    {
        if (index < 0 || index >= Blocks.Count)
        {
            return;
        }

        CurrentIndex = index;
        MarkCurrent();
        ScrollRequested?.Invoke(Blocks[index]);
    }

    private void MarkCurrent()
    {
        foreach (var block in Blocks)
        {
            block.IsCurrent = block.Index == CurrentIndex;
        }
    }
}
