using Avalonia.Controls;
using MyGit.App.Services;
using MyGit.App.ViewModels.Stacks;

namespace MyGit.App.Views;

/// <summary>"New stack from existing branches". Everything happens in <see cref="NewStackViewModel"/>.</summary>
public sealed partial class NewStackWindow : Window
{
    public NewStackWindow()
    {
        InitializeComponent();
        TitleBarIcon.Attach(this);
        Opened += (_, _) =>
        {
            if (DataContext is NewStackViewModel viewModel)
            {
                viewModel.Finished += Close;
            }
        };
    }
}
