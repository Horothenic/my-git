namespace MyGit.Core.Settings;

/// <summary>
/// App-wide preferences that are not tied to one repository.
/// </summary>
public sealed class AppSettings
{
    /// <summary>The repository that was open when the app last closed.</summary>
    public string? LastRepositoryPath { get; set; }

    /// <summary>The folder the last clone went into (the parent, not the repo folder).</summary>
    public string? LastCloneFolder { get; set; }

    /// <summary>Show Unstaged and Staged as flat lists of paths instead of folder trees.</summary>
    public bool FlatFileList { get; set; }

    /// <summary>"Dark" or "Light".</summary>
    public string Theme { get; set; } = "Dark";

    /// <summary>The code editor for "Open in editor" (an id like "vscode"); null picks the first one installed.</summary>
    public string? Editor { get; set; }

    /// <summary>The terminal for "Open in terminal" (an id like "windowsterminal"); null picks the first one installed.</summary>
    public string? Terminal { get; set; }

    /// <summary>The merge tool listed first for conflicts (its name, e.g. "VS Code"); null lists them as found.</summary>
    public string? MergeTool { get; set; }

    /// <summary>Whether "stash my changes and bring them back" starts ticked before a rebase.</summary>
    public bool AutoStashByDefault { get; set; } = true;
}
