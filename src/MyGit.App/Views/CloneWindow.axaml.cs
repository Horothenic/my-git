using System.ComponentModel;
using Avalonia.Controls;
using MyGit.App.Services;
using MyGit.App.ViewModels;

namespace MyGit.App.Views;

public sealed partial class CloneWindow : Window
{
    private const double FormWidth = 560;
    private const double FormHeight = 600;
    private const double PopupWidth = 460;

    public CloneWindow()
    {
        InitializeComponent();
        TitleBarIcon.Attach(this);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is CloneViewModel viewModel)
        {
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    // The form is a resizable dialog; while cloning the window shrinks to a compact popup.
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(CloneViewModel.IsForm) || sender is not CloneViewModel viewModel)
        {
            return;
        }

        if (viewModel.IsForm)
        {
            SizeToContent = SizeToContent.Manual;
            CanResize = true;
            MinHeight = 460;
            Width = FormWidth;
            Height = FormHeight;
        }
        else
        {
            CanResize = false;
            MinHeight = 0;
            Width = PopupWidth;
            SizeToContent = SizeToContent.Height;
        }
    }
}
