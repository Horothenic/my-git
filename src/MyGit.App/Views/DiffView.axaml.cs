using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using MyGit.App.ViewModels.Changes;

namespace MyGit.App.Views;

/// <summary>
/// Mouse and keys for picking lines in the diff:
/// click a red or green side to pick it, Shift+click for a range, Ctrl+click to add, drag to extend;
/// Enter stages (or unstages), Del discards, Esc clears. Nothing happens when the diff is read-only.
/// </summary>
public sealed partial class DiffView : UserControl
{
    // True while the mouse button is held after picking a line, so moving extends the range.
    private bool _dragging;

    public DiffView()
    {
        InitializeComponent();

        // Tunnel, so the list doesn't treat the click as its own selection.
        DiffList.AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
        DiffList.AddHandler(PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel);
        DiffList.AddHandler(PointerReleasedEvent, (_, _) => _dragging = false, RoutingStrategies.Tunnel);
        DiffList.KeyDown += OnKeyDown;
    }

    private DiffViewModel? ViewModel => DataContext as DiffViewModel;

    /// <summary>The red/green side under the pointer, with its row and which side it is.</summary>
    private static (DiffRowViewModel Row, DiffSide Side)? SideAt(object? source)
    {
        for (var visual = source as Visual; visual is not null; visual = visual.GetVisualParent())
        {
            if (visual is Border { Tag: string tag, DataContext: DiffRowViewModel row } border && border.Classes.Contains("diff-side"))
            {
                return (row, tag == "Left" ? DiffSide.Left : DiffSide.Right);
            }
        }

        return null;
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ViewModel is not { CanEditLines: true } viewModel ||
            !e.GetCurrentPoint(DiffList).Properties.IsLeftButtonPressed ||
            SideAt(e.Source) is not { } hit)
        {
            return;
        }

        var ctrl = e.KeyModifiers.HasFlag(Services.Hotkeys.Command); // ⌘ on a Mac
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        viewModel.Pick(hit.Row, hit.Side, ctrl, shift);

        _dragging = !ctrl && !shift && hit.Row.RefFor(hit.Side) is not null;
        DiffList.Focus(); // so Enter / Del / Esc reach the diff
        e.Handled = true;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_dragging || ViewModel is not { } viewModel || !e.GetCurrentPoint(DiffList).Properties.IsLeftButtonPressed)
        {
            _dragging = false;
            return;
        }

        if (SideAt(DiffList.InputHitTest(e.GetPosition(DiffList))) is { } hit)
        {
            viewModel.DragTo(hit.Row, hit.Side);
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (ViewModel is not { HasSelection: true } viewModel || e.KeyModifiers != KeyModifiers.None)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Enter:
                viewModel.ToggleStageLinesCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Delete when viewModel.IsUnstaged:
                viewModel.DiscardLinesCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Escape:
                viewModel.ClearPickedCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }
}
