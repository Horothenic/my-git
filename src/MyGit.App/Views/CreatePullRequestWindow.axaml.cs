using Avalonia.Controls;
using MyGit.App.Services;
using MyGit.App.ViewModels.PullRequests;

namespace MyGit.App.Views;

/// <summary>The Create pull request dialog. Inserting the commit list goes where the cursor is in the description.</summary>
public sealed partial class CreatePullRequestWindow : Window
{
    private int _bodyCaret;

    public CreatePullRequestWindow()
    {
        InitializeComponent();
        TitleBarIcon.Attach(this);

        // Clicking the link takes focus from the description; remember where the cursor was.
        BodyBox.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.CaretIndexProperty)
            {
                _bodyCaret = BodyBox.CaretIndex;
            }
        };

        InsertCommitsButton.Click += (_, _) =>
        {
            if (DataContext is CreatePullRequestViewModel viewModel)
            {
                var caret = viewModel.InsertCommitList(_bodyCaret);
                BodyBox.Focus();
                BodyBox.CaretIndex = caret;
            }
        };

        Opened += (_, _) =>
        {
            if (DataContext is CreatePullRequestViewModel viewModel)
            {
                viewModel.Finished += Close;
                viewModel.ConfirmReplaceDescription = async () =>
                {
                    var confirm = new ConfirmWindow("Replace your description?", "The template replaces what you typed in the description.", "Replace");
                    await confirm.ShowDialog(this);
                    return confirm.Confirmed;
                };
            }

            TitleBox.Focus();
            TitleBox.CaretIndex = TitleBox.Text?.Length ?? 0;
        };
    }
}
