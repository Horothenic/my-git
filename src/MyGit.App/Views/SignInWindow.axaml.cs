using Avalonia.Controls;
using MyGit.App.Services;

namespace MyGit.App.Views;

public sealed partial class SignInWindow : Window
{
    public SignInWindow()
    {
        InitializeComponent();
        TitleBarIcon.Attach(this);
    }
}
