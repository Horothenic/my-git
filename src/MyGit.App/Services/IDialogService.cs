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

    /// <summary>Asks with several buttons. Returns the clicked index, or -1 if the dialog was closed.</summary>
    Task<int> ChooseAsync(string title, string message, IReadOnlyList<string> choices, int dangerIndex = -1);

    /// <summary>Asks for a name (branch, tag). Returns null when cancelled.</summary>
    Task<NameDialogResult?> AskNameAsync(NameDialogOptions options);

    /// <summary>Asks how far to reset (soft / mixed / hard). Returns null when cancelled.</summary>
    Task<MyGit.Core.History.ResetMode?> AskResetAsync(string branch, string shortSha, int commitsUndone);
}
