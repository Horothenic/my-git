using Avalonia;
using Avalonia.Styling;
using MyGit.Core.Settings;

namespace MyGit.App.Services;

/// <summary>
/// Switches between the Dark and Light theme and remembers the choice.
/// </summary>
public sealed class ThemeService
{
    private readonly AppSettingsStore _settingsStore;

    public ThemeService(AppSettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
    }

    public bool IsDark => _settingsStore.Settings.Theme != "Light";

    /// <summary>Applies the saved theme. Call once at startup.</summary>
    public void ApplySaved() => Apply(IsDark);

    public void Toggle() => SetDark(!IsDark);

    /// <summary>Switches to Dark (or Light) and remembers it.</summary>
    public void SetDark(bool dark)
    {
        _settingsStore.Settings.Theme = dark ? "Dark" : "Light";
        _settingsStore.Save();
        Apply(dark);
    }

    private static void Apply(bool dark)
    {
        if (Application.Current is not null)
        {
            Application.Current.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        }
    }
}
