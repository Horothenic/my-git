using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyGit.Core.Operations;

namespace MyGit.App.ViewModels.Changes;

/// <summary>One conflict in the resolver: both sides, and what was chosen.</summary>
public sealed partial class ConflictBlockViewModel(ConflictBlock block, int index) : ObservableObject
{
    public ConflictBlock Block { get; } = block;

    public int Index { get; } = index;

    public string OursText => Lines(Block.Ours);

    public string TheirsText => Lines(Block.Theirs);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsResolved))]
    public partial ConflictChoice Choice { get; set; } = ConflictChoice.Unresolved;

    public bool IsResolved => Choice != ConflictChoice.Unresolved;

    private static string Lines(IReadOnlyList<string> lines) => lines.Count == 0 ? "(nothing here: this side removed these lines)" : string.Join('\n', lines);
}

/// <summary>
/// The built-in 3-way resolver for one conflicted text file: both sides of the current conflict,
/// Take one / Take both / step through conflicts, and the editable result.
/// </summary>
public sealed partial class ConflictResolverViewModel : ObservableObject
{
    private ConflictFile? _file;
    private bool _rebuilding;
    private Func<string, Task>? _save;

    /// <summary>True when a conflicted text file is loaded (otherwise the plain helpers are shown).</summary>
    [ObservableProperty]
    public partial bool IsActive { get; set; }

    public ObservableCollection<ConflictBlockViewModel> Blocks { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Current), nameof(PositionText), nameof(CurrentOursText), nameof(CurrentTheirsText))]
    [NotifyCanExecuteChangedFor(nameof(PreviousCommand), nameof(NextCommand))]
    public partial int CurrentIndex { get; set; }

    public ConflictBlockViewModel? Current => CurrentIndex >= 0 && CurrentIndex < Blocks.Count ? Blocks[CurrentIndex] : null;

    public string CurrentOursText => Current?.OursText ?? string.Empty;

    public string CurrentTheirsText => Current?.TheirsText ?? string.Empty;

    public string PositionText => Blocks.Count == 0 ? string.Empty :
        $"Conflict {CurrentIndex + 1} of {Blocks.Count}" + (UnresolvedCount == 0 ? " · all decided" : $" · {UnresolvedCount} left");

    public int UnresolvedCount => Blocks.Count(block => !block.IsResolved);

    /// <summary>Labels for the two sides, e.g. "main (rebasing onto)" and "yours: “Add slot limit”".</summary>
    [ObservableProperty]
    public partial string OursLabel { get; set; } = "ours";

    [ObservableProperty]
    public partial string TheirsLabel { get; set; } = "theirs";

    [ObservableProperty]
    public partial string TakeOursText { get; set; } = "Take ours";

    [ObservableProperty]
    public partial string TakeTheirsText { get; set; } = "Take theirs";

    /// <summary>The whole file as it will be saved. Editable: type to fine-tune the result.</summary>
    [ObservableProperty]
    public partial string ResultText { get; set; } = string.Empty;

    /// <summary>True after typing in the result; taking a side then rebuilds it (the note says so).</summary>
    [ObservableProperty]
    public partial bool IsEditedByHand { get; set; }

    /// <param name="save">Writes the result to the file and marks it resolved (done by the Changes tab).</param>
    public void Load(ConflictFile file, string oursLabel, string theirsLabel, string takeOursText, string takeTheirsText, Func<string, Task> save)
    {
        _file = file;
        _save = save;
        OursLabel = oursLabel;
        TheirsLabel = theirsLabel;
        TakeOursText = takeOursText;
        TakeTheirsText = takeTheirsText;

        Blocks.Clear();
        for (var i = 0; i < file.Conflicts.Count; i++)
        {
            Blocks.Add(new ConflictBlockViewModel(file.Conflicts[i], i));
        }

        CurrentIndex = 0;
        CurrentChanged(); // the index may already have been 0, so announce the new file's first conflict
        IsEditedByHand = false;
        Rebuild();
        IsActive = true;
    }

    public void Clear()
    {
        _file = null;
        Blocks.Clear();
        ResultText = string.Empty;
        IsActive = false;
    }

    partial void OnResultTextChanged(string value)
    {
        if (!_rebuilding)
        {
            IsEditedByHand = true;
        }
    }

    [RelayCommand]
    private void TakeOurs() => Choose(ConflictChoice.Ours);

    [RelayCommand]
    private void TakeTheirs() => Choose(ConflictChoice.Theirs);

    [RelayCommand]
    private void TakeBoth() => Choose(ConflictChoice.Both);

    [RelayCommand(CanExecute = nameof(CanGoPrevious))]
    private void Previous() => CurrentIndex--;

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private void Next() => CurrentIndex++;

    /// <summary>Saves the result to the file; the Changes tab marks it resolved if no markers are left.</summary>
    [RelayCommand]
    private Task SaveAsync() => _save?.Invoke(ResultText) ?? Task.CompletedTask;

    private bool CanGoPrevious() => CurrentIndex > 0;

    private bool CanGoNext() => CurrentIndex < Blocks.Count - 1;

    private void Choose(ConflictChoice choice)
    {
        if (Current is not { } current)
        {
            return;
        }

        current.Choice = choice;
        IsEditedByHand = false;
        Rebuild();

        // Move on to the next undecided conflict, if any.
        var next = Blocks.Skip(CurrentIndex + 1).Concat(Blocks.Take(CurrentIndex)).FirstOrDefault(block => !block.IsResolved);
        if (next is not null)
        {
            CurrentIndex = next.Index;
        }

        OnPropertyChanged(nameof(PositionText));
        OnPropertyChanged(nameof(UnresolvedCount));
    }

    private void CurrentChanged()
    {
        OnPropertyChanged(nameof(Current));
        OnPropertyChanged(nameof(CurrentOursText));
        OnPropertyChanged(nameof(CurrentTheirsText));
        PreviousCommand.NotifyCanExecuteChanged();
        NextCommand.NotifyCanExecuteChanged();
    }

    private void Rebuild()
    {
        if (_file is null)
        {
            return;
        }

        _rebuilding = true;
        ResultText = _file.Build(Blocks.Select(block => block.Choice).ToList());
        _rebuilding = false;
        OnPropertyChanged(nameof(PositionText));
        OnPropertyChanged(nameof(UnresolvedCount));
    }
}
