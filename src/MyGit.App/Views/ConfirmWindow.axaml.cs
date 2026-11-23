using Avalonia.Controls;
using MyGit.App.Services;

namespace MyGit.App.Views;

/// <summary>
/// A small "are you sure?" dialog with a red confirm button. Cancel is focused first,
/// so pressing Enter by reflex never destroys anything.
/// </summary>
public sealed partial class ConfirmWindow : Window
{
    // Needed by the XAML loader and previewer.
    public ConfirmWindow()
        : this(string.Empty, string.Empty, "OK")
    {
    }

    public ConfirmWindow(string title, string message, string confirmText)
    {
        InitializeComponent();
        TitleBarIcon.Attach(this);

        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmText;

        ConfirmButton.Click += (_, _) =>
        {
            Confirmed = true;
            Close();
        };
        CancelButton.Click += (_, _) => Close();
        Opened += (_, _) => CancelButton.Focus();
    }

    public bool Confirmed { get; private set; }
}
