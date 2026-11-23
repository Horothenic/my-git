using Avalonia.Controls;
using MyGit.App.Services;

namespace MyGit.App.Views;

/// <summary>
/// A dialog with a question and a few buttons, e.g. "Pull, then push" / "Force push…" / "Cancel".
/// <see cref="Choice"/> is the index of the clicked button, or -1 when closed with the X.
/// </summary>
public sealed partial class ChoiceWindow : Window
{
    // Needed by the XAML loader and previewer.
    public ChoiceWindow()
        : this(string.Empty, string.Empty, ["OK"], dangerIndex: -1)
    {
    }

    /// <param name="choices">Button texts, left to right. The first is the recommended action.</param>
    /// <param name="dangerIndex">Which button is destructive (shown red), or -1.</param>
    public ChoiceWindow(string title, string message, IReadOnlyList<string> choices, int dangerIndex)
    {
        InitializeComponent();
        TitleBarIcon.Attach(this);

        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;

        Button? last = null;
        for (var i = 0; i < choices.Count; i++)
        {
            var index = i;
            var button = new Button { Content = choices[i] };

            if (i == dangerIndex)
            {
                button.Classes.Add("danger");
            }
            else if (i == 0)
            {
                button.Classes.Add("accent");
            }

            button.Click += (_, _) =>
            {
                Choice = index;
                Close();
            };

            Buttons.Children.Add(button);
            last = button;
        }

        // Focus the last button (Cancel) so Enter by reflex never runs anything destructive.
        Opened += (_, _) => last?.Focus();
    }

    public int Choice { get; private set; } = -1;
}
