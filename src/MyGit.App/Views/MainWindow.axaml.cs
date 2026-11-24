using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using MyGit.App.Services;
using MyGit.App.ViewModels;

namespace MyGit.App.Views;

/// <summary>
/// The main window. Besides the toolbar wiring: the command palette's keys (↑ ↓ Enter Esc),
/// and Ctrl+B / Ctrl+R to open the Branch and Repository dropdowns with their filter focused.
/// </summary>
public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        TitleBarIcon.Attach(this);

        // Pick up branch switches made outside the app (terminal, IDE) when the user comes back.
        Activated += (_, _) => ViewModel?.RefreshCommand.Execute(null);

        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
        PaletteBox.AddHandler(KeyDownEvent, OnPaletteKeyDown, RoutingStrategies.Tunnel);
        PaletteList.AddHandler(TappedEvent, (_, _) => ViewModel?.Palette.RunSelected(), RoutingStrategies.Bubble);

        // A click outside the palette closes it.
        PaletteOverlay.PointerPressed += (_, e) =>
        {
            if (e.Source == PaletteOverlay)
            {
                ViewModel?.Palette.Close();
            }
        };
    }

    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (ViewModel is not null)
        {
            ViewModel.RepositoryPicker.CloseRequested += () => RepositoryButton.Flyout?.Hide();
            ViewModel.Branches.CloseRequested += () => BranchButton.Flyout?.Hide();
            if (BranchButton.Flyout is PopupFlyoutBase branchFlyout)
            {
                branchFlyout.Opening += (_, _) => _ = ViewModel.Branches.LoadAsync(ViewModel.CurrentRepository?.Path);
            }
            ViewModel.DialogOpening += () => RepositoryButton.Flyout?.Hide();
            ViewModel.Account.DialogOpening += () => AccountButton.Flyout?.Hide();

            ViewModel.Palette.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(CommandPaletteViewModel.IsOpen) && ViewModel.Palette.IsOpen)
                {
                    Dispatcher.UIThread.Post(() => PaletteBox.Focus(), DispatcherPriority.Input);
                }
            };
        }
    }

    // Ctrl+B / Ctrl+R: the Branch / Repository dropdown, ready to type a filter.
    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.Control || ViewModel is not { HasRepository: true } && e.Key == Key.B)
        {
            return;
        }

        var button = e.Key switch
        {
            Key.B => BranchButton,
            Key.R => RepositoryButton,
            _ => null,
        };

        if (button?.Flyout is not Flyout { Content: Control content } flyout)
        {
            return;
        }

        e.Handled = true;
        flyout.ShowAt(button);
        Dispatcher.UIThread.Post(() => content.GetLogicalDescendants().OfType<TextBox>().FirstOrDefault()?.Focus(), DispatcherPriority.Input);
    }

    private void OnPaletteKeyDown(object? sender, KeyEventArgs e)
    {
        if (ViewModel?.Palette is not { } palette)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Down:
                palette.Move(+1);
                break;
            case Key.Up:
                palette.Move(-1);
                break;
            case Key.Enter:
                palette.RunSelected();
                break;
            case Key.Escape:
                palette.Close();
                break;
            default:
                return;
        }

        if (palette.SelectedItem is { } selected)
        {
            PaletteList.ScrollIntoView(selected);
        }

        e.Handled = true;
    }
}
