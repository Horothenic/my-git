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
        : this(title, message, confirmText, optionText: null, optionDefault: false, dangerous: true)
    {
    }

    /// <param name="optionText">An optional tickbox under the message (null hides it).</param>
    /// <param name="dangerous">Red confirm button (destructive) or the normal accent one.</param>
    public ConfirmWindow(string title, string message, string confirmText, string? optionText, bool optionDefault, bool dangerous)
    {
        InitializeComponent();
        TitleBarIcon.Attach(this);

        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmText;

        if (!dangerous)
        {
            ConfirmButton.Classes.Remove("danger");
            ConfirmButton.Classes.Add("accent");
        }

        if (optionText is not null)
        {
            Option.IsVisible = true;
            Option.Content = optionText;
            Option.IsChecked = optionDefault;
        }

        ConfirmButton.Click += (_, _) =>
        {
            Confirmed = true;
            Close();
        };
        CancelButton.Click += (_, _) => Close();
        Opened += (_, _) => CancelButton.Focus();
    }

    public bool Confirmed { get; private set; }

    public bool IsOptionChecked => Option.IsChecked == true;
}
