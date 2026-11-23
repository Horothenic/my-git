using Avalonia.Controls;
using MyGit.App.ViewModels;

namespace MyGit.App.Views;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

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
        }
    }
}
