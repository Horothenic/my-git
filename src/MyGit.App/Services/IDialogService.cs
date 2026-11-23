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
}
