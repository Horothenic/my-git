using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Avalonia.Input;
using Avalonia.Interactivity;
using MyGit.App.ViewModels.Changes;

namespace MyGit.App.Views;

/// <summary>
/// Keyboard and mouse handling for the Changes tab. The view model does the work; this file
/// only turns keys, double-clicks and menu clicks into calls with the selected tree nodes.
///
/// Keys: Enter = stage / unstage, Del = discard, H = hide / unhide,
///       Ctrl+H = show hidden files, Ctrl+Enter = commit.
/// </summary>
public sealed partial class ChangesView : UserControl
{
    // Set while one tree clears the other's selection, so that doesn't clear the diff.
    private bool _syncingSelection;

    public ChangesView()
    {
        InitializeComponent();

        UnstagedTree.SelectionChanged += (_, _) => OnSelectionChanged(UnstagedTree, StagedTree, staged: false);
        StagedTree.SelectionChanged += (_, _) => OnSelectionChanged(StagedTree, UnstagedTree, staged: true);

        UnstagedTree.KeyDown += (_, e) => OnTreeKeyDown(e, staged: false);
        StagedTree.KeyDown += (_, e) => OnTreeKeyDown(e, staged: true);

        UnstagedTree.DoubleTapped += (_, e) => OnTreeDoubleTapped(e, staged: false);
        StagedTree.DoubleTapped += (_, e) => OnTreeDoubleTapped(e, staged: true);

        // Unstaged menu
        UnstagedStageItem.Click += (_, _) => Run(vm => vm.StageAsync(Selection(UnstagedTree)));
        UnstagedDiscardItem.Click += (_, _) => Run(vm => vm.DiscardAsync(Selection(UnstagedTree)));
        UnstagedHideItem.Click += (_, _) => Run(vm => vm.ToggleHiddenAsync(Selection(UnstagedTree)));
        UnstagedOpenItem.Click += (_, _) => WithFirst(UnstagedTree, (vm, node) => vm.OpenInEditor(node));
        UnstagedExplorerItem.Click += (_, _) => WithFirst(UnstagedTree, (vm, node) => vm.ShowInExplorer(node));
        UnstagedCopyItem.Click += (_, _) => WithFirst(UnstagedTree, (vm, node) => _ = vm.CopyPathAsync(node));
        UnstagedTree.ContextMenu!.Opening += (_, _) =>
            UnstagedHideItem.Header = ViewModel?.IsShowingHidden == true ? "Unhide" : "Hide";

        // Staged menu
        StagedUnstageItem.Click += (_, _) => Run(vm => vm.UnstageAsync(Selection(StagedTree)));
        StagedOpenItem.Click += (_, _) => WithFirst(StagedTree, (vm, node) => vm.OpenInEditor(node));
        StagedExplorerItem.Click += (_, _) => WithFirst(StagedTree, (vm, node) => vm.ShowInExplorer(node));
        StagedCopyItem.Click += (_, _) => WithFirst(StagedTree, (vm, node) => _ = vm.CopyPathAsync(node));

        // Shortcuts that work anywhere in the tab, even while typing the commit message.
        AddHandler(KeyDownEvent, OnViewKeyDown, RoutingStrategies.Tunnel);

        // Picking lines in the diff (tunnel, so the list doesn't treat the click as its own selection).
        DiffList.AddHandler(PointerPressedEvent, OnDiffPointerPressed, RoutingStrategies.Tunnel);
        DiffList.AddHandler(PointerMovedEvent, OnDiffPointerMoved, RoutingStrategies.Tunnel);
        DiffList.AddHandler(PointerReleasedEvent, (_, _) => _dragging = false, RoutingStrategies.Tunnel);
        DiffList.KeyDown += OnDiffKeyDown;
    }

    // True while the mouse button is held after picking a line, so moving extends the range.
    private bool _dragging;

    private ChangesViewModel? ViewModel => DataContext as ChangesViewModel;

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

    private void OnDiffPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ViewModel is not { } viewModel || !e.GetCurrentPoint(DiffList).Properties.IsLeftButtonPressed || SideAt(e.Source) is not { } hit)
        {
            return;
        }

        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        viewModel.Diff.Pick(hit.Row, hit.Side, ctrl, shift);

        _dragging = !ctrl && !shift && hit.Row.RefFor(hit.Side) is not null;
        DiffList.Focus(); // so Enter / Del / Esc reach the diff
        e.Handled = true;
    }

    private void OnDiffPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_dragging || ViewModel is not { } viewModel || !e.GetCurrentPoint(DiffList).Properties.IsLeftButtonPressed)
        {
            _dragging = false;
            return;
        }

        if (SideAt(DiffList.InputHitTest(e.GetPosition(DiffList))) is { } hit)
        {
            viewModel.Diff.DragTo(hit.Row, hit.Side);
        }
    }

    private void OnDiffKeyDown(object? sender, KeyEventArgs e)
    {
        if (ViewModel is not { Diff.HasSelection: true } viewModel || e.KeyModifiers != KeyModifiers.None)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Enter:
                viewModel.ToggleStageLinesCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Delete when viewModel.Diff.IsUnstaged:
                viewModel.DiscardLinesCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Escape:
                viewModel.Diff.ClearPickedCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    private static List<FileTreeNodeViewModel> Selection(TreeView tree) =>
        tree.SelectedItems.OfType<FileTreeNodeViewModel>().ToList();

    private void OnSelectionChanged(TreeView tree, TreeView other, bool staged)
    {
        if (_syncingSelection || ViewModel is not { } viewModel)
        {
            return;
        }

        var selection = Selection(tree);

        if (selection.Count > 0 && other.SelectedItems.Count > 0)
        {
            _syncingSelection = true;
            other.SelectedItems.Clear();
            _syncingSelection = false;
        }

        // Show the most recently selected file.
        var file = tree.SelectedItem as FileTreeNodeViewModel ?? selection.LastOrDefault();
        if (file is { IsFile: true })
        {
            _ = viewModel.SelectFileAsync(file, staged);
        }
        else if (selection.Count == 0 && other.SelectedItems.Count == 0)
        {
            _ = viewModel.SelectFileAsync(null, staged);
        }
    }

    private void OnTreeKeyDown(KeyEventArgs e, bool staged)
    {
        if (e.KeyModifiers != KeyModifiers.None)
        {
            return;
        }

        var tree = staged ? StagedTree : UnstagedTree;

        switch (e.Key)
        {
            case Key.Enter:
                Run(vm => vm.ToggleStageAsync(Selection(tree), staged));
                e.Handled = true;
                break;
            case Key.Delete when !staged:
                Run(vm => vm.DiscardAsync(Selection(tree)));
                e.Handled = true;
                break;
            case Key.H when !staged:
                Run(vm => vm.ToggleHiddenAsync(Selection(tree)));
                e.Handled = true;
                break;
        }
    }

    private void OnTreeDoubleTapped(TappedEventArgs e, bool staged)
    {
        // Double-click on a file or folder row moves it; double-clicking the expander arrow doesn't.
        if (e.Source is Control { DataContext: FileTreeNodeViewModel node })
        {
            Run(vm => vm.ToggleStageAsync([node], staged));
        }
    }

    private void OnViewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.Control || ViewModel is not { } viewModel)
        {
            return;
        }

        if (e.Key == Key.H)
        {
            viewModel.ToggleHiddenViewCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            viewModel.CommitCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void Run(Func<ChangesViewModel, Task> action)
    {
        if (ViewModel is { } viewModel)
        {
            _ = action(viewModel);
        }
    }

    private void WithFirst(TreeView tree, Action<ChangesViewModel, FileTreeNodeViewModel> action)
    {
        if (ViewModel is { } viewModel && Selection(tree).FirstOrDefault() is { } node)
        {
            action(viewModel, node);
        }
    }
}
