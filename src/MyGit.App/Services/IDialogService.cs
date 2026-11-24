namespace MyGit.App.Services;

/// <summary>
/// Opens the app's dialogs. View models call this instead of creating windows themselves.
/// </summary>
public interface IDialogService
{
    /// <summary>Shows the GitHub sign-in dialog. Returns true if the user signed in.</summary>
    Task<bool> ShowSignInAsync();

    /// <summary>Shows the clone dialog. Returns the cloned folder, or null if cancelled.</summary>
    Task<string?> ShowCloneAsync();

    /// <summary>Asks a yes/no question with a red confirm button. Returns true if confirmed.</summary>
    Task<bool> ConfirmAsync(string title, string message, string confirmText);

    /// <summary>Like <see cref="ConfirmAsync"/>, with a tickbox. Returns whether confirmed, and the tickbox state.</summary>
    Task<(bool Confirmed, bool Option)> ConfirmWithOptionAsync(string title, string message, string confirmText, string optionText, bool optionDefault, bool dangerous);

    /// <summary>Asks with several buttons. Returns the clicked index, or -1 if the dialog was closed.</summary>
    Task<int> ChooseAsync(string title, string message, IReadOnlyList<string> choices, int dangerIndex = -1);

    /// <summary>Asks for a name (branch, tag). Returns null when cancelled.</summary>
    Task<NameDialogResult?> AskNameAsync(NameDialogOptions options);

    /// <summary>Shows the interactive rebase window. Returns true when the user pressed Start.</summary>
    Task<bool> ShowInteractiveRebaseAsync(ViewModels.Rebase.InteractiveRebaseViewModel viewModel);

    /// <summary>Shows the Create pull request dialog. Returns true when GitHub created it.</summary>
    Task<bool> ShowCreatePullRequestAsync(ViewModels.PullRequests.CreatePullRequestViewModel viewModel);

    /// <summary>Shows "New stack from existing branches". Returns true when the user pressed Create stack.</summary>
    Task<bool> ShowNewStackAsync(ViewModels.Stacks.NewStackViewModel viewModel);

    /// <summary>Asks how far to reset (soft / mixed / hard). Returns null when cancelled.</summary>
    Task<MyGit.Core.History.ResetMode?> AskResetAsync(string branch, string shortSha, int commitsUndone);
}
