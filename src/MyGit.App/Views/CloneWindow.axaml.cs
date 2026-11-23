using Avalonia.Controls;
using MyGit.App.Services;

namespace MyGit.App.Views;

public sealed partial class CloneWindow : Window
{
    public CloneWindow()
    {
        InitializeComponent();
        TitleBarIcon.Attach(this);
    }
}
