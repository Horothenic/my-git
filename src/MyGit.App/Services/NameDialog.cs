namespace MyGit.App.Services;

/// <summary>
/// What the "name something" dialog asks for (used for new branches and tags).
/// </summary>
/// <param name="Title">e.g. "Create branch"</param>
/// <param name="Description">One line under the title, e.g. "From a1b2c3d Inventory slots".</param>
/// <param name="Placeholder">Example name shown in the empty box.</param>
/// <param name="ConfirmText">Text of the main button.</param>
/// <param name="Validate">Returns why a name can't be used, or null when it can.</param>
/// <param name="CheckBoxText">An optional tickbox (null to hide it).</param>
/// <param name="CheckBoxDefault">Whether the tickbox starts ticked.</param>
/// <param name="MessagePlaceholder">Shows an optional multi-line message box when set (for annotated tags).</param>
public sealed record NameDialogOptions(
    string Title,
    string Description,
    string Placeholder,
    string ConfirmText,
    Func<string, Task<string?>> Validate,
    string? CheckBoxText = null,
    bool CheckBoxDefault = false,
    string? MessagePlaceholder = null,
    string? SecondCheckBoxText = null,
    bool AllowEmpty = false);

/// <summary>What the user entered.</summary>
public sealed record NameDialogResult(string Name, string? Message, bool IsChecked, bool IsSecondChecked);
