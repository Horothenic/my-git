using MyGit.Core.Diffs;

namespace MyGit.App.ViewModels.Changes;

/// <summary>
/// One row of the side-by-side diff, flattened for binding.
/// </summary>
public sealed class DiffRowViewModel
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

        RightNumber = row.Right?.NewNumber?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        RightText = row.Right?.Text ?? string.Empty;
        IsRightAdded = row.Right?.Kind == DiffLineKind.Added;
        IsRightEmpty = row.Kind == SideBySideRowKind.Change && row.Right is null;
    }

    public bool IsHunkHeader { get; }

    public bool IsLine => !IsHunkHeader;

    public string HunkHeader { get; }

    public int HunkIndex { get; }

    public string LeftNumber { get; }

    public string LeftText { get; }

    public bool IsLeftRemoved { get; }

    /// <summary>The left side has no line on this row (a pure addition): shown hatched/dim.</summary>
    public bool IsLeftEmpty { get; }

    public string RightNumber { get; }

    public string RightText { get; }

    public bool IsRightAdded { get; }

    public bool IsRightEmpty { get; }
}
