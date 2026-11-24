using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using MyGit.App.Services;
using MyGit.App.ViewModels.Rebase;

namespace MyGit.App.Views;

/// <summary>
/// The interactive rebase window. Rows are reordered by dragging their handle or with Alt+↑/↓,
/// and P / R / E / S / F / D set the selected row's action.
/// </summary>
public sealed partial class InteractiveRebaseWindow : Window
{
    private RebaseRowViewModel? _dragged;

    public InteractiveRebaseWindow()
    {
        InitializeComponent();
        TitleBarIcon.Attach(this);

        RowList.AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
        RowList.AddHandler(PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel);
        RowList.AddHandler(PointerReleasedEvent, (_, _) => _dragged = null, RoutingStrategies.Tunnel);
        RowList.PointerCaptureLost += (_, _) => _dragged = null;
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);

        Opened += (_, _) =>
        {
            RowList.Focus();
            if (DataContext is InteractiveRebaseViewModel viewModel)
            {
                viewModel.Finished += Close;
            }
        };
    }

    private InteractiveRebaseViewModel? ViewModel => DataContext as InteractiveRebaseViewModel;

    // Pressing a row's handle starts a drag; the list then follows the pointer.
    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var grip = (e.Source as Visual)?.FindAncestorOfType<Border>(includeSelf: true);
        while (grip is not null && !grip.Classes.Contains("grip"))
        {
            grip = grip.FindAncestorOfType<Border>();
        }

        if (grip?.DataContext is RebaseRowViewModel row && ViewModel is { } viewModel)
        {
            _dragged = row;
            viewModel.SelectedRow = row;
            e.Pointer.Capture(RowList);
            e.Handled = true;
        }
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragged is null || ViewModel is not { } viewModel)
        {
            return;
        }

        var y = e.GetPosition(RowList).Y;
        for (var index = 0; index < viewModel.Rows.Count; index++)
        {
            if (RowList.ContainerFromIndex(index) is not { } container ||
                container.TranslatePoint(default, RowList) is not { } top)
            {
                continue;
            }

            if (y >= top.Y && y < top.Y + container.Bounds.Height)
            {
                viewModel.Move(_dragged, index);
                break;
            }
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        if (e.KeyModifiers == KeyModifiers.Alt && e.Key is Key.Up or Key.Down)
        {
            (e.Key == Key.Up ? viewModel.MoveUpCommand : viewModel.MoveDownCommand).Execute(null);
            RowList.ScrollIntoView(viewModel.SelectedRow!);
            e.Handled = true;
            return;
        }

        // Letters set the action, except while typing a message or choosing in an open dropdown.
        var typing = e.Source is TextBox || (e.Source as Visual)?.FindAncestorOfType<TextBox>() is not null;
        var dropdownOpen = (e.Source as Visual)?.FindAncestorOfType<ComboBox>(includeSelf: true) is { IsDropDownOpen: true };
        if (typing || dropdownOpen || e.KeyModifiers != KeyModifiers.None || e.Key is < Key.A or > Key.Z)
        {
            return;
        }

        if (viewModel.SetActionFromKey((char)('A' + (e.Key - Key.A))))
        {
            e.Handled = true;
        }
    }
}
