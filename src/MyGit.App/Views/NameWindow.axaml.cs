using Avalonia.Controls;
using MyGit.App.Services;

namespace MyGit.App.Views;

/// <summary>
/// Asks for a name (new branch, new tag) and checks it as you type.
/// <see cref="Result"/> is null when cancelled.
/// </summary>
public sealed partial class NameWindow : Window
{
    private readonly NameDialogOptions _options;
    private int _validation; // ignores answers to older keystrokes

    // Needed by the XAML loader and previewer.
    public NameWindow()
        : this(new NameDialogOptions("Name", string.Empty, string.Empty, "OK", _ => Task.FromResult<string?>(null)))
    {
    }

    public NameWindow(NameDialogOptions options)
    {
        InitializeComponent();
        TitleBarIcon.Attach(this);
        _options = options;

        Title = options.Title;
        TitleText.Text = options.Title;
        DescriptionText.Text = options.Description;
        NameBox.PlaceholderText = options.Placeholder;
        ConfirmButton.Content = options.ConfirmText;

        if (options.MessagePlaceholder is { } messagePlaceholder)
        {
            MessageBox.IsVisible = true;
            MessageBox.PlaceholderText = messagePlaceholder;
        }

        if (options.CheckBoxText is { } checkText)
        {
            Option.IsVisible = true;
            Option.Content = checkText;
            Option.IsChecked = options.CheckBoxDefault;
        }

        if (options.SecondCheckBoxText is { } secondText)
        {
            SecondOption.IsVisible = true;
            SecondOption.Content = secondText;
        }

        NameBox.TextChanged += async (_, _) => await ValidateAsync();
        CancelButton.Click += (_, _) => Close();
        ConfirmButton.Click += async (_, _) =>
        {
            if (await ValidateAsync())
            {
                Result = new NameDialogResult(NameBox.Text!.Trim(), MessageBox.Text, Option.IsChecked == true, SecondOption.IsChecked == true);
                Close();
            }
        };
        Opened += (_, _) => NameBox.Focus();
    }

    public NameDialogResult? Result { get; private set; }

    private async Task<bool> ValidateAsync()
    {
        var attempt = ++_validation;
        var name = NameBox.Text?.Trim() ?? string.Empty;

        if (name.Length == 0)
        {
            ErrorText.IsVisible = false;
            ConfirmButton.IsEnabled = false;
            return false;
        }

        var problem = await _options.Validate(name);
        if (attempt != _validation)
        {
            return false; // the user kept typing
        }

        ErrorText.Text = problem;
        ErrorText.IsVisible = problem is not null;
        ConfirmButton.IsEnabled = problem is null;
        return problem is null;
    }
}
