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

    public void Toggle()
    {
        _settingsStore.Settings.Theme = IsDark ? "Light" : "Dark";
        _settingsStore.Save();
        Apply(IsDark);
    }

    private static void Apply(bool dark)
    {
        if (Application.Current is not null)
        {
            Application.Current.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        }
    }
}
