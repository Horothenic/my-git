using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyGit.Core.Stacks;

namespace MyGit.App.ViewModels.Stacks;

/// <summary>A branch in the New stack dialog: include it or not, and move it up or down.</summary>
public sealed partial class CandidateRowViewModel(string name, string note, bool isIncluded, bool isTop, NewStackViewModel owner) : ObservableObject
{
    public string Name { get; } = name;

    /// <summary>e.g. "2 commits", "1 commit · top", "same commit as ui".</summary>
    [ObservableProperty]
    public partial string Note { get; set; } = note;

    [ObservableProperty]
    public partial bool IsIncluded { get; set; } = isIncluded;

    /// <summary>The chosen top branch is always in.</summary>
    public bool IsTop { get; } = isTop;

    public bool CanToggle => !IsTop;

    partial void OnIsIncludedChanged(bool value) => owner.Update();

    [RelayCommand]
    private void MoveUp() => owner.Move(this, -1);

    [RelayCommand]
    private void MoveDown() => owner.Move(this, +1);
}

/// <summary>
/// "New stack from existing branches": pick the top branch; the branches below it (down to the trunk) are
/// suggested, ticked, in order. Untick the ones that don't belong, add others, reorder if needed.
/// </summary>
public sealed partial class NewStackViewModel : ObservableObject
{
    private readonly Func<string, Task<IReadOnlyList<StackCandidate>>> _findCandidates;

    /// <param name="branches">Local branches that can top a stack (not the trunk, not already stacked).</param>
    /// <param name="findCandidates">For a top branch: the branches below it.</param>
    public NewStackViewModel(string trunk, IReadOnlyList<string> branches, string? suggestedTop, Func<string, Task<IReadOnlyList<StackCandidate>>> findCandidates)
    {
        Trunk = trunk;
        _findCandidates = findCandidates;
        foreach (var branch in branches)
        {
            Branches.Add(branch);
        }

        SelectedTop = suggestedTop is not null && branches.Contains(suggestedTop) ? suggestedTop : branches.FirstOrDefault();
    }

    /// <summary>Raised when the window should close.</summary>
    public event Action? Finished;

    public string Trunk { get; }

    public ObservableCollection<string> Branches { get; } = [];

    [ObservableProperty]
    public partial string? SelectedTop { get; set; }

    /// <summary>Top first, like the Stacks tab draws them.</summary>
    public ObservableCollection<CandidateRowViewModel> Rows { get; } = [];

    /// <summary>Local branches not in the list yet, for "Also include".</summary>
    public ObservableCollection<string> Others { get; } = [];

    [ObservableProperty]
    public partial string? OtherToAdd { get; set; }

    [ObservableProperty]
    public partial string Summary { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    public partial bool HasChoice { get; set; }

    /// <summary>The chosen branches, bottom first, once Create was pressed (else null).</summary>
    public IReadOnlyList<string>? Result { get; private set; }

    public async Task LoadAsync()
    {
        if (SelectedTop is not { } top)
        {
            Rows.Clear();
            Update();
            return;
        }

        var candidates = await _findCandidates(top);
        if (top != SelectedTop)
        {
            return; // another top was picked meanwhile
        }

        Rows.Clear();
        foreach (var candidate in candidates)
        {
            var commits = candidate.CommitCount == 1 ? "1 commit" : $"{candidate.CommitCount} commits";
            var note = candidate.Name == top ? $"{commits} · top" : candidate.SameCommitAs is { } same ? $"same commit as {same}" : commits;
            Rows.Add(new CandidateRowViewModel(candidate.Name, note, candidate.IsSuggested, candidate.Name == top, this));
        }

        Update();
    }

    public void Move(CandidateRowViewModel row, int by)
    {
        var index = Rows.IndexOf(row);
        var target = index + by;
        if (index >= 0 && target >= 0 && target < Rows.Count)
        {
            Rows.Move(index, target);
            Update();
        }
    }

    /// <summary>Refreshes the summary line and the "Also include" list.</summary>
    public void Update()
    {
        var chain = Rows.Where(row => row.IsIncluded).Select(row => row.Name).Reverse().ToList();
        Summary = chain.Count == 0 ? string.Empty : $"Stack: {Trunk} → {string.Join(" → ", chain)}. Each PR goes into the branch below it.";
        HasChoice = chain.Count > 0;

        Others.Clear();
        foreach (var branch in Branches.Where(branch => Rows.All(row => row.Name != branch)))
        {
            Others.Add(branch);
        }
    }

    partial void OnSelectedTopChanged(string? value) => _ = LoadAsync();

    partial void OnOtherToAddChanged(string? value)
    {
        if (value is null || Rows.Any(row => row.Name == value))
        {
            return;
        }

        // Goes in just above the trunk; move it where it belongs.
        Rows.Add(new CandidateRowViewModel(value, "added by hand: move it into place", isIncluded: true, isTop: false, this));
        OtherToAdd = null;
        Update();
    }

    [RelayCommand(CanExecute = nameof(HasChoice))]
    private void Create()
    {
        Result = Rows.Where(row => row.IsIncluded).Select(row => row.Name).Reverse().ToList();
        Finished?.Invoke();
    }

    [RelayCommand]
    private void Cancel() => Finished?.Invoke();
}
