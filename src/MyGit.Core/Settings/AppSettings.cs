namespace MyGit.Core.Settings;

/// <summary>
/// App-wide preferences that are not tied to one repository.
/// </summary>
public sealed class AppSettings
{
    /// <summary>The repository that was open when the app last closed.</summary>
    public string? LastRepositoryPath { get; set; }

    /// <summary>"Dark" or "Light".</summary>
    public string Theme { get; set; } = "Dark";
}
