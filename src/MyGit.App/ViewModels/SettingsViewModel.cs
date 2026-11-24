using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyGit.App.Services;
using MyGit.Core.Settings;
using MyGit.Core.Tools;

namespace MyGit.App.ViewModels;

/// <summary>A choice in one of the Settings dropdowns; <see cref="Id"/> is null for "Automatic" / "Ask each time".</summary>
public sealed record SettingOption(string? Id, string Name)
{
    public override string ToString() => Name;
}

/// <summary>
/// The Settings window: appearance, tools, defaults and the GitHub account. Every change is saved right away.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AppSettingsStore _settings;
    private readonly ThemeService _theme;
    private readonly IFolderPicker _folderPicker;
    private readonly Action<bool> _setFlat;
    private readonly bool _loading;

    /// <param name="setFlat">Shows the Changes tab's files as a flat list (true) or folder tree.</param>
    public SettingsViewModel(AppSettingsStore settings, ThemeService theme, AccountViewModel account, IFolderPicker folderPicker, Action<bool> setFlat)
    {
        _settings = settings;
        _theme = theme;
        _folderPicker = folderPicker;
        _setFlat = setFlat;
        Account = account;

        Editors.Add(new SettingOption(null, Automatic(ToolLocator.Editors, "no code editor found")));
        foreach (var editor in ToolLocator.Editors)
        {
            Editors.Add(new SettingOption(editor.Id, editor.Name));
        }

        Terminals.Add(new SettingOption(null, Automatic(ToolLocator.Terminals, "no terminal found")));
        foreach (var terminal in ToolLocator.Terminals)
        {
            Terminals.Add(new SettingOption(terminal.Id, terminal.Name));
        }

        foreach (var name in new[] { "VS Code", "Rider", "UnityYAMLMerge" })
        {
            MergeTools.Add(new SettingOption(name, name));
        }

        _loading = true;
        IsDark = theme.IsDark;
        IsFlat = settings.Settings.FlatFileList;
        SelectedEditor = Editors.FirstOrDefault(option => option.Id == settings.Settings.Editor) ?? Editors[0];
        SelectedTerminal = Terminals.FirstOrDefault(option => option.Id == settings.Settings.Terminal) ?? Terminals[0];
        SelectedMergeTool = MergeTools.FirstOrDefault(option => option.Id == settings.Settings.MergeTool) ?? MergeTools[0];
        AutoStashByDefault = settings.Settings.AutoStashByDefault;
        CloneFolder = settings.Settings.LastCloneFolder ?? DefaultCloneFolder;
        _loading = false;
    }

    /// <summary>Raised when the window should close.</summary>
    public event Action? Finished;

    public AccountViewModel Account { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLight))]
    public partial bool IsDark { get; set; }

    public bool IsLight
    {
        get => !IsDark;
        set => IsDark = !value;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTree))]
    public partial bool IsFlat { get; set; }

    public bool IsTree
    {
        get => !IsFlat;
        set => IsFlat = !value;
    }

    public ObservableCollection<SettingOption> Editors { get; } = [];

    [ObservableProperty]
    public partial SettingOption? SelectedEditor { get; set; }

    public ObservableCollection<SettingOption> Terminals { get; } = [];

    [ObservableProperty]
    public partial SettingOption? SelectedTerminal { get; set; }

    /// <summary>"Ask each time" (the menu as found), or the one listed first.</summary>
    public ObservableCollection<SettingOption> MergeTools { get; } = [new SettingOption(null, "Ask each time")];

    [ObservableProperty]
    public partial SettingOption? SelectedMergeTool { get; set; }

    [ObservableProperty]
    public partial bool AutoStashByDefault { get; set; }

    [ObservableProperty]
    public partial string CloneFolder { get; set; } = string.Empty;

    private static string DefaultCloneFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents", "Repositories");

    partial void OnIsDarkChanged(bool value)
    {
        if (!_loading)
        {
            _theme.SetDark(value);
        }
    }

    partial void OnIsFlatChanged(bool value)
    {
        if (!_loading)
        {
            _setFlat(value);
        }
    }

    partial void OnSelectedEditorChanged(SettingOption? value) => Save(settings => settings.Editor = value?.Id);

    partial void OnSelectedTerminalChanged(SettingOption? value) => Save(settings => settings.Terminal = value?.Id);

    partial void OnSelectedMergeToolChanged(SettingOption? value) => Save(settings => settings.MergeTool = value?.Id);

    partial void OnAutoStashByDefaultChanged(bool value) => Save(settings => settings.AutoStashByDefault = value);

    [RelayCommand]
    private async Task ChangeCloneFolderAsync()
    {
        if (await _folderPicker.PickFolderAsync("Where clones go") is { } folder)
        {
            CloneFolder = folder;
            Save(settings => settings.LastCloneFolder = folder);
        }
    }

    [RelayCommand]
    private void Done() => Finished?.Invoke();

    private void Save(Action<AppSettings> change)
    {
        if (_loading)
        {
            return;
        }

        change(_settings.Settings);
        _settings.Save();
    }

    private static string Automatic(IReadOnlyList<ExternalTool> found, string none) =>
        found.Count == 0 ? $"Automatic ({none})" : $"Automatic ({found[0].Name})";
}
