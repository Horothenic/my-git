using Avalonia.Controls;
using MyGit.App.Services;
using MyGit.App.ViewModels;

namespace MyGit.App.Views;

/// <summary>The list of keyboard shortcuts (from <see cref="MainWindowViewModel.Shortcuts"/>).</summary>
public sealed partial class ShortcutsWindow : Window
{
    /// <summary>A row of the list.</summary>
    public sealed record Row(string Keys, string Action);

    public ShortcutsWindow()
    {
        InitializeComponent();
        TitleBarIcon.Attach(this);
        List.ItemsSource = MainWindowViewModel.Shortcuts.Select(shortcut => new Row(shortcut.Keys, shortcut.Action)).ToList();
        CloseButton.Click += (_, _) => Close();
    }
}
