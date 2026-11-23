using Avalonia.Controls;
using MyGit.App.Services;
using MyGit.Core.History;

namespace MyGit.App.Views;

/// <summary>
/// "Reset branch to here": three plain-language choices. <see cref="Result"/> is null when cancelled.
/// </summary>
public sealed partial class ResetWindow : Window
{
    // Needed by the XAML loader and previewer.
    public ResetWindow()
        : this("main", "a1b2c3d", 1)
    {
    }

    public ResetWindow(string branch, string shortSha, int commitsUndone)
    {
        InitializeComponent();
        TitleBarIcon.Attach(this);

        Title = $"Reset {branch}";
        TitleText.Text = $"Reset {branch} to {shortSha}?";

        var undone = commitsUndone switch
        {
            0 => "No commits are undone",
            1 => "Undoes the 1 commit after it",
            _ => $"Undoes the {commitsUndone} commits after it",
        };
        SoftText.Text = $"{undone}; their changes wait in Staged.";

        CancelButton.Click += (_, _) => Close();
        ResetButton.Click += (_, _) =>
        {
            Result = HardOption.IsChecked == true ? ResetMode.Hard : SoftOption.IsChecked == true ? ResetMode.Soft : ResetMode.Mixed;
            Close();
        };
        Opened += (_, _) => MixedOption.Focus();
    }

    public ResetMode? Result { get; private set; }
}
