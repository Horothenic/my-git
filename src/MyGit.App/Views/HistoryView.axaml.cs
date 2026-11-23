using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using MyGit.App.ViewModels.History;

namespace MyGit.App.Views;

/// <summary>
/// The History tab's list: right-click menu actions on the clicked commit, and loading more
/// commits as you scroll toward the end.
/// </summary>
public sealed partial class HistoryView : UserControl
{
    private CommitRowViewModel? _menuCommit;

    public HistoryView()
    {
        InitializeComponent();

        // Right-click selects the row under the pointer first, so the menu acts on what you clicked.
        CommitList.AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);

        CommitMenu.Opening += (_, _) =>
        {
            _menuCommit = ViewModel?.SelectedCommit;
            var hasTag = _menuCommit?.HasTag == true;
            BranchFromTagItem.IsVisible = hasTag;
            BranchFromTagItem.Header = hasTag ? $"Create branch from tag {_menuCommit!.FirstTag}…" : "Create branch from tag…";
        };

        CreateBranchItem.Click += (_, _) => WithCommit((vm, commit) => vm.CreateBranchAsync(commit, fromTag: false));
        BranchFromTagItem.Click += (_, _) => WithCommit((vm, commit) => vm.CreateBranchAsync(commit, fromTag: true));
        CreateTagItem.Click += (_, _) => WithCommit((vm, commit) => vm.CreateTagAsync(commit));
        CheckoutItem.Click += (_, _) => WithCommit((vm, commit) => vm.CheckoutAsync(commit));
        CopyShaItem.Click += (_, _) => WithCommit((vm, commit) => vm.CopyShaAsync(commit));
        CopyMessageItem.Click += (_, _) => WithCommit((vm, commit) => vm.CopyMessageAsync(commit));

        // Load the next page when the last rows come into view.
        CommitList.ContainerPrepared += (_, e) =>
        {
            if (ViewModel is { } viewModel && e.Index >= viewModel.Commits.Count - 30)
            {
                _ = viewModel.LoadMoreAsync();
            }
        };
    }

    private HistoryViewModel? ViewModel => DataContext as HistoryViewModel;

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(CommitList).Properties.IsRightButtonPressed || ViewModel is not { } viewModel)
        {
            return;
        }

        if ((e.Source as Control)?.FindAncestorOfType<ListBoxItem>(includeSelf: true)?.DataContext is CommitRowViewModel row)
        {
            viewModel.SelectedCommit = row;
        }
    }

    private void WithCommit(Func<HistoryViewModel, CommitRowViewModel, Task> action)
    {
        if (ViewModel is { } viewModel && _menuCommit is { } commit)
        {
            _ = action(viewModel, commit);
        }
    }
}
