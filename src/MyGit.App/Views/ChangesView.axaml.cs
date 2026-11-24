using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Avalonia.Input;
using Avalonia.Animation;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
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

        UnstagedStashItem.Click += (_, _) => Run(vm => vm.StashAsync(Selection(UnstagedTree)));

        // The resolver's ↑/↓ (and each choice) scroll to the conflict they land on.
        DataContextChanged += (_, _) =>
        {
            if (ViewModel is { } viewModel)
            {
                viewModel.Resolver.ScrollRequested -= ScrollToConflict;
                viewModel.Resolver.ScrollRequested += ScrollToConflict;
                viewModel.Resolver.SaveRefused -= ShakeSaveButton;
                viewModel.Resolver.SaveRefused += ShakeSaveButton;
            }
        };

        // A conflict takes over the right side; picking a normal file gives it back.
        UnstagedTree.SelectionChanged += (_, _) => { if (UnstagedTree.SelectedItems.Count > 0) { ConflictList.SelectedItem = null; } };
        StagedTree.SelectionChanged += (_, _) => { if (StagedTree.SelectedItems.Count > 0) { ConflictList.SelectedItem = null; } };

        // Picking a file in a tree takes the diff back from a stash file, and the other way round.
        StashFilesList.SelectionChanged += (_, _) =>
        {
            if (StashFilesList.SelectedItem is not null)
            {
                _syncingSelection = true;
                UnstagedTree.SelectedItems.Clear();
                StagedTree.SelectedItems.Clear();
                _syncingSelection = false;
            }
        };

        // Shortcuts that work anywhere in the tab, even while typing the commit message.
        AddHandler(KeyDownEvent, OnViewKeyDown, RoutingStrategies.Tunnel);

    }

    private ChangesViewModel? ViewModel => DataContext as ChangesViewModel;

    private void ScrollToConflict(ConflictBlockViewModel block) =>
        ResolverSegments.ContainerFromItem(block)?.BringIntoView();

    // A quick side-to-side shake: "not yet, some conflicts are undecided".
    private static readonly Animation Shake = new()
    {
        Duration = TimeSpan.FromMilliseconds(400),
        Children =
        {
            ShakeFrame(0.0, 0),
            ShakeFrame(0.15, -7),
            ShakeFrame(0.3, 7),
            ShakeFrame(0.45, -5),
            ShakeFrame(0.6, 5),
            ShakeFrame(0.8, -2),
            ShakeFrame(1.0, 0),
        },
    };

    private void ShakeSaveButton() => _ = Shake.RunAsync(SaveResolvedButton);

    private static KeyFrame ShakeFrame(double cue, double x) => new()
    {
        Cue = new Cue(cue),
        Setters = { new Setter(TranslateTransform.XProperty, x) },
    };

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

        if (selection.Count > 0)
        {
            StashFilesList.SelectedItem = null;
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
        var tree = staged ? StagedTree : UnstagedTree;

        // Ctrl+A selects every file shown (then Enter stages or unstages them all).
        if (e.KeyModifiers == Services.Hotkeys.Command && e.Key == Key.A)
        {
            tree.SelectAll();
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers != KeyModifiers.None)
        {
            return;
        }

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
        if (e.KeyModifiers != Services.Hotkeys.Command || ViewModel is not { } viewModel)
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
