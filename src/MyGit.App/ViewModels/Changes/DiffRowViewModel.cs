using CommunityToolkit.Mvvm.ComponentModel;
using MyGit.Core.Diffs;

namespace MyGit.App.ViewModels.Changes;

public enum DiffSide
{
    Left,
    Right,
}

/// <summary>
/// One row of the side-by-side diff, flattened for binding.
/// Changed lines can be picked (clicked) to stage, unstage or discard just them.
/// </summary>
public sealed partial class DiffRowViewModel : ObservableObject
{
    public DiffRowViewModel(SideBySideRow row)
    {
        IsHunkHeader = row.Kind == SideBySideRowKind.HunkHeader;
        HunkHeader = row.HunkHeader ?? string.Empty;
        HunkIndex = row.HunkIndex;

        LeftNumber = row.Left?.OldNumber?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        LeftText = row.Left?.Text ?? string.Empty;
        IsLeftRemoved = row.Left?.Kind == DiffLineKind.Removed;
        IsLeftEmpty = row.Kind == SideBySideRowKind.Change && row.Left is null;
        LeftRef = IsLeftRemoved ? new DiffLineRef(row.HunkIndex, row.LeftIndex) : null;

        RightNumber = row.Right?.NewNumber?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        RightText = row.Right?.Text ?? string.Empty;
        IsRightAdded = row.Right?.Kind == DiffLineKind.Added;
        IsRightEmpty = row.Kind == SideBySideRowKind.Change && row.Right is null;
        RightRef = IsRightAdded ? new DiffLineRef(row.HunkIndex, row.RightIndex) : null;
    }

    public bool IsHunkHeader { get; }

    public bool IsLine => !IsHunkHeader;

    public string HunkHeader { get; }

    public int HunkIndex { get; }

    public string LeftNumber { get; }

    public string LeftText { get; }

    public bool IsLeftRemoved { get; }

    /// <summary>The left side has no line on this row (a pure addition): shown dim.</summary>
    public bool IsLeftEmpty { get; }

    /// <summary>Which diff line the left side is, when it's a removal that can be picked.</summary>
    public DiffLineRef? LeftRef { get; }

    public string RightNumber { get; }

    public string RightText { get; }

    public bool IsRightAdded { get; }

    public bool IsRightEmpty { get; }

    /// <summary>Which diff line the right side is, when it's an addition that can be picked.</summary>
    public DiffLineRef? RightRef { get; }

    [ObservableProperty]
    public partial bool IsLeftSelected { get; set; }

    [ObservableProperty]
    public partial bool IsRightSelected { get; set; }

    public DiffLineRef? RefFor(DiffSide side) => side == DiffSide.Left ? LeftRef : RightRef;

    public void SetSelected(DiffSide side, bool selected)
    {
        if (side == DiffSide.Left)
        {
            IsLeftSelected = selected;
        }
        else
        {
            IsRightSelected = selected;
        }
    }
}
