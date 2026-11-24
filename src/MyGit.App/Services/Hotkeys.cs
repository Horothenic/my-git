using Avalonia.Input;

namespace MyGit.App.Services;

/// <summary>
/// Shortcut keys per platform: Ctrl on Windows, ⌘ (Command) on a Mac. Shortcuts are written once the
/// Windows way ("Ctrl+Shift+P") and shown the Mac way ("⌘⇧P") there.
/// </summary>
public static class Hotkeys
{
    public static bool IsMac { get; } = OperatingSystem.IsMacOS();

    /// <summary>The modifier for app shortcuts: Ctrl, or ⌘ on a Mac.</summary>
    public static KeyModifiers Command { get; } = IsMac ? KeyModifiers.Meta : KeyModifiers.Control;

    /// <summary>"Ctrl+Shift+P" as this platform writes it: unchanged on Windows, "⌘⇧P" on a Mac.</summary>
    public static string Show(string shortcut) => !IsMac
        ? shortcut
        : shortcut
            .Replace("Ctrl+", "⌘", StringComparison.Ordinal)
            .Replace("Shift+", "⇧", StringComparison.Ordinal)
            .Replace("Alt+", "⌥", StringComparison.Ordinal)
            .Replace("Enter", "↵", StringComparison.Ordinal)
            .Replace("Delete", "⌫", StringComparison.Ordinal);

    /// <summary>Tooltip of the hidden-files button.</summary>
    public static string ShowHiddenTip => $"Show hidden files ({Show("Ctrl+H")}). Select files and press H to hide or unhide them.";

    /// <summary>Tooltip of the commit button.</summary>
    public static string CommitTip => Show("Ctrl+Enter");

    /// <summary>The diff's line-picking hint.</summary>
    public static string DiffPickTip => $"Shift+click for a range · {(IsMac ? "⌘" : "Ctrl+")}click to add or remove";
}
