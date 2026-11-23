using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using MyGit.App.Services;
using MyGit.App.ViewModels;

namespace MyGit.App.Views;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        TitleBarIcon.Attach(this);

        // Pick up branch switches made outside the app (terminal, IDE) when the user comes back.
        Activated += (_, _) => ViewModel?.RefreshCommand.Execute(null);
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
        }
    }
}
