using Avalonia.Controls;
using MyGit.App.Services;
using MyGit.App.ViewModels;

namespace MyGit.App.Views;

/// <summary>The Settings window. Everything happens in <see cref="SettingsViewModel"/>.</summary>
public sealed partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        TitleBarIcon.Attach(this);
        Opened += (_, _) =>
        {
            if (DataContext is SettingsViewModel viewModel)
            {
                viewModel.Finished += Close;
            }
        };
    }
}
